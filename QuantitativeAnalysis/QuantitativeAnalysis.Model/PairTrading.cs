﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuantitativeAnalysis.Model
{
    public class pairDifference
    {
        public string code1 { get; set; }
        public string code2 { get; set; }
        public DateTime date { get; set; }
        public double difference { get; set; }
    }

    public class pairOfStocks
    {
        public string code1 { get; set; }
        public string code2 { get; set; }
        public DateTime ipoTime1 { get; set; }
        public DateTime ipoTime2 { get; set; }
        public DateTime startTime { get; set; }
        public DateTime endTime { get; set; }
    }
}
