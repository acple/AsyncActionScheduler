using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AsyncScheduler;
using ChainingAssertion;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AsyncActionSchedulerTest
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestRun()
        {
            var queue = new AsyncActionScheduler();

            foreach (var x in Enumerable.Range(0, 30))
            {
                queue.Add(_ =>
                {
                    Task.Delay(100).Wait();
                    Trace.WriteLine(x);
                });
                queue.Add(_ =>
                {
                    return x + 3;
                });
                queue.Add(async _ =>
                {
                    await Task.Delay(20);
                    return x.ToString();
                });
            }
            Task.Delay(3000).Wait();
        }

        [TestMethod]
        public async Task LinearTest()
        {
            var queue = new AsyncActionScheduler();
            var counter = 0;

            await Task.WhenAll(Enumerable
                .Range(0, 10)
                .Select(x => queue.Add(async _ =>
                {
                    await Task.Delay(new Random(x).Next(300, 2000)).ConfigureAwait(false);
                    counter.Is(x);
                    counter++;
                })));

            counter.Is(10);
        }
    }
}
