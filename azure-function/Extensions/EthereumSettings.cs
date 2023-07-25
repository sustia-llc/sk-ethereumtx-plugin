public class EthereumSettings : IEthereumSettings
{
    public string EtherscanApiKey { get; set; }

    public EthereumSettings()
    {
        EtherscanApiKey = string.Empty;
    }
}