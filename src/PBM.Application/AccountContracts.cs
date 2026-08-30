namespace PBM.Application;

public sealed record ChangeOwnPasswordRequest(string CurrentPassword, string NewPassword);

public interface IAccountService
{
    Task ChangePasswordAsync(ChangeOwnPasswordRequest request, CancellationToken cancellationToken = default);
}
