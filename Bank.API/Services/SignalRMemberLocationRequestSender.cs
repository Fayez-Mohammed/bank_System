using Bank.API.Hubs;
using Bank.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace Bank.API.Services;

public class SignalRMemberLocationRequestSender : IMemberLocationRequestSender
{
    private readonly IHubContext<MemberLocationHub> _hubContext;

    public SignalRMemberLocationRequestSender(IHubContext<MemberLocationHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task SendLocationRequestAsync(string connectionId, Guid requestId, CancellationToken cancellationToken = default) =>
        _hubContext.Clients.Client(connectionId).SendAsync("LocationRequested", requestId, cancellationToken);
}
