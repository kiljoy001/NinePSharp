using System.Threading.Tasks;

namespace NinePSharp.Tests.Helpers;

internal static class TestTaskExtensions
{
    public static void Sync(this Task task) => task.GetAwaiter().GetResult();

    public static T Sync<T>(this Task<T> task) => task.GetAwaiter().GetResult();
}
