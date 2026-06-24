using System.Text;
using Parser.Storage;

namespace Parser.Test
{
    public class SimpleStoreTests
    {
        private static byte[] ToBytes(string s) => Encoding.UTF8.GetBytes(s);

        [Fact]
        public async Task ConcurrentOperations_Statistics()
        {
            const int threadCount = 10;
            const int actionCount = 200;

            using var store = new SimpleStore();

            var setTasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < actionCount; i++)
                    store.Set($"data:{i}", ToBytes("value"));
            }));

            var getTasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < actionCount; i++)
                    store.Get($"data:{i % (threadCount * actionCount)}");
            }));

            var deleteTasks = Enumerable.Range(0, threadCount).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < actionCount; i++)
                    store.Delete($"data:{i}");
            }));

            await Task.WhenAll(setTasks.Concat(getTasks).Concat(deleteTasks));

            var (setCount, getCount, deleteCount) = store.GetStatistics();

            long expectedCount = (long)threadCount * actionCount;

            Assert.Equal(expectedCount, setCount);
            Assert.Equal(expectedCount, getCount);
            Assert.Equal(expectedCount, deleteCount);
        }
    }
}
