// This file is part of Tmds.MDns which is released under MIT.
// See file LICENSE for full license details.

namespace Tmds.MDns
{
    public class QueryParameters
    {
        public QueryParameters()
        {
            StartQueryCount = 2;
            StartQueryInterval = 5000;
            QueryInterval = 10000;
            ResponseTime = 1000;
            Robustness = 2;
        }
        public int StartQueryCount { get; set; }
        public int StartQueryInterval { get; set; }
        public int QueryInterval { get; set; }
        public int ResponseTime { get; set; }
        public int Robustness { get; set; }
    }
}
