using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;

namespace ShipIt.Models.ApiModels
{
    public class OutboundOrderResponseModel
    {
        public List<TruckResponseModel> TruckShipments { get; set; }
        public int RequiredNumberOfTrucks { get; set; }
    }
}
