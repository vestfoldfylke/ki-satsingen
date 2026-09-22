namespace kisatsingen.Services.Chat;
using System.Text.RegularExpressions;

public sealed class MessageValidator
{
    private readonly ILogger<MessageValidator> _logger;
    private readonly Regex _ssnRg = new(@"\d{6}[-\s]?\d{5}");

    public MessageValidator(ILogger<MessageValidator> logger)
    {
        _logger = logger;
    }

    public bool CheckIfMessageContainsSsn(string Message)
    {
        if(!_ssnRg.IsMatch(Message))
        {
            return false;
        }
        _logger.LogWarning("A social security number has been detected!");
        return true;
    }

    public bool CheckIfMessageContainsASpecificString(string Message, string Keyword)
    {
        return Message.Contains(Keyword, StringComparison.OrdinalIgnoreCase);
    }
}