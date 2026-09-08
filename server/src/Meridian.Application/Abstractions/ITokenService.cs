using Meridian.Domain.Entities;

namespace Meridian.Application.Abstractions;

public interface ITokenService
{
    string CreateToken(User user);
}
