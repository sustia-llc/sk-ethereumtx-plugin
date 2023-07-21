public class EtherscanSettings : IEtherscanSettings
{
    public string? EtherscanApiKey { get; set; }

    public EtherscanSettings()
    {
        EtherscanApiKey = string.Empty;
    }
}