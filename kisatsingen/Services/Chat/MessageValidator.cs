namespace kisatsingen.Services.Chat;
using System.Text.RegularExpressions;

public sealed class MessageValidator
{
    private readonly ILogger<MessageValidator> _logger;

    private readonly string SsnPattern = @"\d{6}[-\s]?\d{5}";

    public MessageValidator(ILogger<MessageValidator> logger)
    {
        _logger = logger;
    }

    public bool checkIfMessageContainsSsn(string Message)
    {
        Regex rg = new Regex(SsnPattern);

        if(!rg.IsMatch(Message))
        {
            return false;
        }
        _logger.LogWarning("A social security number has been detected!");
        return true;
    }
}