using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Parser.Helpers
{
    public ref struct ArrayPoolSpanHelper<T> // : IDisposable
    {
        private T[] _data;
        private int _size;
        public Span<T> SpanScoped => _data.AsSpan(0, _size);

        public ArrayPoolSpanHelper(int size)
        {
            _data = ArrayPool<T>.Shared.Rent(size);
            _size = size;
        }

        public void Dispose()
        {
            _size = 0;
            ArrayPool<T>.Shared.Return(_data, true);
        }
    }
}
