using System;
using System.Threading;
using System.Threading.Tasks;

namespace FieldVisitHotFolderService
{
    public class LimitedConcurrencyContext : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        private LimitedConcurrencyContext(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            _semaphore.Release();
        }

        public static async Task<LimitedConcurrencyContext> EnterContextAsync(SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();

            try
            {
                return new LimitedConcurrencyContext(semaphore);
            }
            catch
            {
                semaphore.Release();
                throw;
            }
        }
    }
}
