﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.TagHelpers.Cache;
using ShipIt.Exceptions;
using ShipIt.Models.ApiModels;
using ShipIt.Repositories;

namespace ShipIt.Controllers
{
    [Route("orders/outbound")]
    public class OutboundOrderController : ControllerBase
    {
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod()?.DeclaringType);

        private readonly IStockRepository _stockRepository;
        private readonly IProductRepository _productRepository;

        public OutboundOrderController(IStockRepository stockRepository, IProductRepository productRepository)
        {
            _stockRepository = stockRepository;
            _productRepository = productRepository;
        }

        [HttpPost("")]
        public OutboundOrderResponseModel Post([FromBody] OutboundOrderRequestModel request)
        {
            Log.Info(String.Format("Processing outbound order: {0}", request));

            var gtins = new List<String>();
            foreach (var orderLine in request.OrderLines)
            {
                if (gtins.Contains(orderLine.gtin))
                {
                    throw new ValidationException(String.Format("Outbound order request contains duplicate product gtin: {0}", orderLine.gtin));
                }
                gtins.Add(orderLine.gtin);
            }

            var productDataModels = _productRepository.GetProductsByGtin(gtins);
            var products = productDataModels.ToDictionary(p => p.Gtin, p => new Product(p));

            var lineItems = new List<StockAlteration>();
            var productIds = new List<int>();
            var errors = new List<string>();

            foreach (var orderLine in request.OrderLines)
            {
                if (!products.ContainsKey(orderLine.gtin))
                {
                    errors.Add(string.Format("Unknown product gtin: {0}", orderLine.gtin));
                }
                else
                {
                    var product = products[orderLine.gtin];
                    lineItems.Add(new StockAlteration(product.Id, orderLine.quantity));
                    productIds.Add(product.Id);
                }
            }

            if (errors.Count > 0)
            {
                throw new NoSuchEntityException(string.Join("; ", errors));
            }

            var stock = _stockRepository.GetStockByWarehouseAndProductIds(request.WarehouseId, productIds);

            var orderLines = request.OrderLines.ToList();
            errors = new List<string>();

            for (int i = 0; i < lineItems.Count; i++)
            {
                var lineItem = lineItems[i];
                var orderLine = orderLines[i];

                if (!stock.ContainsKey(lineItem.ProductId))
                {
                    errors.Add(string.Format("Product: {0}, no stock held", orderLine.gtin));
                    continue;
                }

                var item = stock[lineItem.ProductId];
                if (lineItem.Quantity > item.held)
                {
                    errors.Add(
                        string.Format("Product: {0}, stock held: {1}, stock to remove: {2}", orderLine.gtin, item.held,
                            lineItem.Quantity));
                }
            }

            if (errors.Count > 0)
            {
                throw new InsufficientStockException(string.Join("; ", errors));
            }

            _stockRepository.RemoveStock(request.WarehouseId, lineItems);
            var truckShipments = GetTruckShipments(gtins, orderLines);
            var numberOfTrucks = CalculateNumberOfTrucks(truckShipments);


            return new OutboundOrderResponseModel
            {
                TruckShipments = truckShipments,
                RequiredNumberOfTrucks = numberOfTrucks
            };
        }


        public int CalculateNumberOfTrucks(List<TruckResponseModel> trucks)
        {
            return trucks.Count;
        }

        public List<TruckResponseModel> GetTruckShipments(List<string> gtins, IEnumerable<OrderLine> orderLines)
        {
            var productDataModels = _productRepository.GetProductsByGtin(gtins);
            var products = productDataModels.ToDictionary(p => p.Gtin, p => new Product(p));
            var maxTruckWeightKg = 2000;

            var truckShipments = new List<TruckResponseModel>();


            foreach (var orderLine in orderLines)
            {
                float totalProductWeightKg = 0;
                if (products.ContainsKey(orderLine.gtin))
                {
                    var product = products[orderLine.gtin];
                    totalProductWeightKg = product.Weight * orderLine.quantity / 1000;
                }

                var productAdded = false;
                foreach (var truck in truckShipments)
                {
                    if (truck.TotalWeightKg + totalProductWeightKg <= maxTruckWeightKg)
                    {
                        truck.gtinQuantities.Add(orderLine.gtin, orderLine.quantity);
                        truck.TotalWeightKg += totalProductWeightKg;
                        productAdded = true;
                        break;
                    }
                }
                if (!productAdded)
                {
                    var nextTruckShipment = new TruckResponseModel
                    {
                        gtinQuantities = new Dictionary<string, int>(),
                        TotalWeightKg = 0
                    };
                    truckShipments.Add(nextTruckShipment);
                    nextTruckShipment.gtinQuantities.Add(orderLine.gtin, orderLine.quantity);
                    nextTruckShipment.TotalWeightKg += totalProductWeightKg;
                }
            }
            return truckShipments;
        }
    }
}