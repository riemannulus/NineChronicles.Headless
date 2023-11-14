namespace Libplanet.Headless.Hosting
{
    public class StateServiceActionEvaluatorConfiguration : IActionEvaluatorConfiguration
    {
        public ActionEvaluatorType Type => ActionEvaluatorType.StateServiceActionEvaluator;
        public StateServiceConfiguration[] StateServices { get; set; } = null!;
        
        public string StateServiceDownloadPath { get; set; } = null!;
    }
    
    public class StateServiceConfiguration
    {
        public string Path { get; set; } = null!;

        public ushort Port { get; set; } = 11111;
        public StateServiceRange Range { get; set; } = null!;
        public string StateStorePath { get; set; } = null!;
    }

    public class StateServiceRange
    {
        public long Start { get; set; }
        public long End { get; set; }
    }
}
