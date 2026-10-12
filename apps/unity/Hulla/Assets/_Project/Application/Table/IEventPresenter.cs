using Hulla.Application.Services;

namespace Hulla.Application.Table
{
    /// <summary>Shows one applied message and says how long the table should hold before the next one.</summary>
    public interface IEventPresenter
    {
        float Present(ServerMessage message);
    }
}
