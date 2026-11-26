using System;
using Unity.Services.Core;
using Unity.Services.Matchmaker;

/// <summary>Search failure with a short player-facing message; the detail stays in logs.</summary>
public sealed class MatchmakingFailedException : Exception
{
    public string PlayerMessage { get; }

    public MatchmakingFailedException(string playerMessage, string detail, Exception inner = null)
        : base(detail, inner)
    {
        PlayerMessage = playerMessage;
    }
}

/// <summary>Collapses matchmaking errors into a few short FindMatch status lines.</summary>
public static class MatchmakingFailure
{
    #region Messages

    public const string SearchTimedOut = "Quá thời gian tìm trận";
    public const string ServerStartTimedOut = "Máy chủ trận đấu không phản hồi";
    public const string NoConnection = "Không có kết nối mạng";
    public const string ServiceBusy = "Hệ thống đang bận, hãy thử lại sau";

    // Internal codes for failures that have no service error code.
    public const string NetworkManagerMissing = "MM-01";
    public const string NetworkManagerBusy = "MM-02";
    public const string RankUnavailable = "MM-03";
    public const string AssignmentFailed = "MM-04";
    public const string AssignmentInvalid = "MM-05";
    public const string ClientStartFailed = "MM-06";
    public const string Unknown = "MM-00";

    public static string Failed(string code) => $"Tìm trận không thành công ({code})";

    #endregion

    #region Mapping

    public static string Describe(Exception exception)
    {
        switch (exception)
        {
            case MatchmakingFailedException failure:
                return failure.PlayerMessage;
            case MatchmakerServiceException service:
                return DescribeMatchmaker(service);
            case RequestFailedException request:
                return DescribeCommon(request.ErrorCode);
            case TimeoutException _:
                return SearchTimedOut;
            default:
                return Failed(Unknown);
        }
    }

    private static string DescribeMatchmaker(MatchmakerServiceException exception)
    {
        switch (exception.Reason)
        {
            case MatchmakerExceptionReason.NetworkError:
                return NoConnection;
            case MatchmakerExceptionReason.RateLimited:
            case MatchmakerExceptionReason.ServiceUnavailable:
                return ServiceBusy;
            case MatchmakerExceptionReason.GatewayTimeout:
                return SearchTimedOut;
            default:
                return Failed(((int)exception.Reason).ToString());
        }
    }

    private static string DescribeCommon(int errorCode)
    {
        switch (errorCode)
        {
            case CommonErrorCodes.TransportError:
                return NoConnection;
            case CommonErrorCodes.Timeout:
                return SearchTimedOut;
            case CommonErrorCodes.ServiceUnavailable:
            case CommonErrorCodes.TooManyRequests:
                return ServiceBusy;
            default:
                return Failed(errorCode.ToString());
        }
    }

    #endregion
}
