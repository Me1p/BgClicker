using System;

namespace BgClicker
{
    public sealed class ClickTargetSettings
    {
        public bool Browser { get; set; }
        public string Id { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string WsUrl { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public long Deep { get; set; }
        public long Root { get; set; }
        public int DeepX { get; set; }
        public int DeepY { get; set; }
        public int RootX { get; set; }
        public int RootY { get; set; }
        public bool UseRoot { get; set; }
    }
}
