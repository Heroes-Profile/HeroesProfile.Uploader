using Heroesprofile.Uploader.Desktop;
using Xunit;

namespace Heroesprofile.Uploader.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void A_normal_socket_path_is_used_as_is()
    {
        var path = Path.Combine(Path.GetTempPath(), "hp", "instance.sock");
        Assert.Equal(path, SingleInstance.ShortEnoughForASocket(path));
    }

    [Fact]
    public void A_path_too_long_for_a_unix_socket_falls_back_to_a_short_one()
    {
        var deep = Path.Combine(Path.GetTempPath(), new string('x', 80), new string('y', 80), "instance.sock");

        var socket = SingleInstance.ShortEnoughForASocket(deep);

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(socket) <= 104, socket); // macOS' limit, the tightest
        Assert.Equal(socket, SingleInstance.ShortEnoughForASocket(deep)); // stable across launches
    }

    [Fact]
    public void Different_long_paths_get_different_sockets()
    {
        var a = Path.Combine(Path.GetTempPath(), new string('a', 120), "instance.sock");
        var b = Path.Combine(Path.GetTempPath(), new string('b', 120), "instance.sock");

        Assert.NotEqual(SingleInstance.ShortEnoughForASocket(a), SingleInstance.ShortEnoughForASocket(b));
    }
}
