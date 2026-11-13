using Ascolotl.Core.Models;

namespace Ascolotl.Core.Services;

public interface ISessionStore
{
    void Save(SessionState state);
    SessionState? Load();
}
