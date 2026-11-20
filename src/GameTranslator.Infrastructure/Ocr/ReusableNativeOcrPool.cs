namespace GameTranslator.Infrastructure.Ocr;

// Exclusively leased native instances. A failed operation never returns its instance.
internal sealed class ReusableNativeOcrPool<T>(int capacity) : IDisposable where T : class, IDisposable
{
    private readonly object gate = new();
    private readonly Slot[] slots = Enumerable.Range(0, capacity).Select(_ => new Slot()).ToArray();
    private bool disposed;

    internal Lease Rent(string key, Func<T> create)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var slot = slots.FirstOrDefault(s => !s.Busy && s.Key == key && s.Value is not null)
                ?? slots.FirstOrDefault(s => !s.Busy)
                ?? throw new InvalidOperationException("Native OCR pool capacity exceeded.");
            if (slot.Key != key)
            {
                slot.Value?.Dispose();
                slot.Value = null;
                slot.Key = key;
            }
            slot.Value ??= create();
            slot.Busy = true;
            return new Lease(this, slot, slot.Value);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var slot in slots.Where(s => !s.Busy))
            {
                slot.Value?.Dispose();
                slot.Value = null;
            }
        }
    }

    private void Return(Slot slot, bool reusable)
    {
        lock (gate)
        {
            if (!reusable || disposed)
            {
                slot.Value?.Dispose();
                slot.Value = null;
            }
            slot.Busy = false;
        }
    }

    internal sealed class Slot
    {
        internal string? Key;
        internal T? Value;
        internal bool Busy;
    }

    internal sealed class Lease(ReusableNativeOcrPool<T> owner, Slot slot, T value) : IDisposable
    {
        private bool returned;
        internal T Value { get; } = value;
        internal bool Reusable { get; set; }
        public void Dispose()
        {
            if (returned) return;
            returned = true;
            owner.Return(slot, Reusable);
        }
    }
}
