using System.Collections.Generic;
using System.Data;

namespace ShipIt.Models.ApiModels;

public class TruckResponseModel
{
   public Dictionary<string, int> gtinQuantities { get; set; }
   public float TotalWeightKg { get; set; }
}