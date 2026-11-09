namespace Ohrbob.Server.Shared.Requests;

public class StripeCheckoutRequest
{
    public string AccountId { get; set; } = string.Empty;
    public string SuccessUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
}

public class StripePortalRequest
{
    public string AccountId { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
}

public class StripeSessionResponse
{
    public string Url { get; set; } = string.Empty;
}
