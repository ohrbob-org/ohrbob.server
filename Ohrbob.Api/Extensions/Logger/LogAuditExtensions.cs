namespace Ohrbob.Api.Extensions.Logger;

using Microsoft.Extensions.Logging;
using Serilog.Context;

public static class LogAuditExtensions
{

    private const string AuditPropertyName = "IsAuditEvent";

    public static void LogAudit(this ILogger logger, string messageTemplate, params object[] propertyValues)
    {
        using (LogContext.PushProperty(AuditPropertyName, true))
        {
            logger.LogInformation(messageTemplate, propertyValues);
        }
    }

    public static void LogAudit(this ILogger logger, string action, string userId, object details)
    {
        using (LogContext.PushProperty(AuditPropertyName, true))
        using (LogContext.PushProperty("AuditEvent", new { Action = action, UserId = userId, Details = details }, destructureObjects: true))
        {
            logger.LogInformation("Audit Action: {AuditAction} performed by {AuditUserId}", action, userId);
        }
    }
}
