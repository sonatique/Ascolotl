using Ascolotl.Core.Models;
using Ascolotl.Core.Services;

namespace Ascolotl.Tests.Unit.Mocks;

public sealed class MockSessionStore : ISessionStore
{
    public SessionState? NextLoadResult { get; set; }
    public SessionState? LastSavedState { get; private set; }
    public int SaveCalled { get; private set; }
    public int LoadCalled { get; private set; }

    public void Save(SessionState state)
    {
        SaveCalled++;
        LastSavedState = state;
    }

    public SessionState? Load()
    {
        LoadCalled++;
        return NextLoadResult;
    }
}
