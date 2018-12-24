﻿using System;
using System.Threading;
using System.Diagnostics;

internal class TransferBuffer<T>
{
    private static readonly Log Log = Logs.Create(typeof(TransferBuffer<T>).Name);

    private readonly T[] _buffer;
    private volatile int _readHead;
    private volatile int _unread;
    private volatile int _writeHead;

    private readonly T[] _singleReadItem = new T[1];
    private readonly T[] _singleWriteItem = new T[1];

    /// <summary>
    /// Get an estimate of the amount of data in the buffer. This is only an estimate because of data races with other threads
    /// </summary>
    public int EstimatedUnreadCount
    {
        get { return _unread; }
    }

    public int Capacity
    {
        get { return _buffer.Length; }
    }

    public TransferBuffer(int capacity = 4096)
    {
        _buffer = new T[capacity];
    }

    #region write

    /// <summary>
    /// Attempt to write a single item into the buffer
    /// </summary>
    /// <param name="item"></param>
    /// <returns>true if the write succeeded, otherwise false</returns>
    public bool TryWrite(T item)
    {
        _singleWriteItem[0] = item;
        var success = TryWriteAll(new ArraySegment<T>(_singleWriteItem));
        _singleWriteItem[0] = default(T);
        return success;
    }

    /// <summary>
    /// Try to write a segment into the buffer
    /// </summary>
    /// <param name="data"></param>
    /// <returns>true if the entire segment was writte, false is none was written</returns>
    public bool TryWriteAll(ArraySegment<T> data)
    {
        // check if we have enough space in the buffer
        if (_unread + data.Count > _buffer.Length)
            return false;

        if (_writeHead + data.Count > _buffer.Length)
        {
            // going to run off the end of the buffer;
            // copy as much as we can then put the rest at the start of the buffer
            var remainingSpace = _buffer.Length - _writeHead;
            // ReSharper disable once AssignNullToNotNullAttribute (Justification: Array segment cannot be null)
            Array.Copy(data.Array, data.Offset, _buffer, _writeHead, remainingSpace);
            Array.Copy(data.Array, data.Offset + remainingSpace, _buffer, 0, data.Count - remainingSpace);
            _writeHead = (_writeHead + data.Count) % _buffer.Length;
        }
        else
        {
            // copy the data into the buffer
            // ReSharper disable once AssignNullToNotNullAttribute (Justification: Array segment cannot be null)
            Array.Copy(data.Array, data.Offset, _buffer, _writeHead, data.Count);
            _writeHead += data.Count;
        }

#pragma warning disable 420
        // Justification: It's Interlocked, so volatile isn't important (See: http://stackoverflow.com/a/425150/108234 )
        Interlocked.Add(ref _unread, data.Count);
#pragma warning restore 420

        return true;
    }

    /// <summary>
    /// Writes as much of the data into the buffer as possible
    /// </summary>
    /// <param name="data"></param>
    /// <returns>The amount of items written into the buffer</returns>
    public int WriteSome(ArraySegment<T> data)
    {
        // Write either the entire input buffer or the amount of space remaining in the internal buffer, whichever is less
        var writeCount = Math.Min(_buffer.Length - _unread, data.Count);
        if (writeCount == 0)
            return 0;

        // Write the amount we just calculated (it can only fail if there is insufficient buffer capacity, and that's not possible because we...
        // ...calculated the value to ensure there is sufficient capacity). If this ever happens it was probably due to concurrent writes, which...
        // ...are not allowed.

        TryWriteAll(new ArraySegment<T>(data.Array, data.Offset, writeCount));

        return writeCount;
    }

    #endregion

    #region read

    public bool Read([CanBeNull] out T item)
    {
        var success = Read(_singleReadItem);
        item = success ? _singleReadItem[0] : default(T);
        _singleReadItem[0] = default(T);
        return success;
    }

    public bool Read([NotNull] T[] data)
    {
        return Read(new ArraySegment<T>(data, 0, data.Length));
    }

    public bool Read([NotNull] T[] data, int readCount)
    {
        if (readCount > data.Length)
            throw new ArgumentException("Requested read amount is > size of supplied output buffer", "readCount");

        return Read(new ArraySegment<T>(data, 0, readCount));
    }

    public bool Read(ArraySegment<T> data)
    {
        if (_unread < data.Count)
            return false;

        if (_readHead + data.Count > _buffer.Length)
        {
            // going to run off the end of the buffer;
            // copy as much as we can then start reading the rest from the start of the buffer
            var remainingSpace = _buffer.Length - _readHead;
            // ReSharper disable once AssignNullToNotNullAttribute (Justification: Array segment cannot be null)
            Array.Copy(_buffer, _readHead, data.Array, data.Offset, remainingSpace);
            Array.Copy(_buffer, 0, data.Array, data.Offset + remainingSpace, data.Count - remainingSpace);
            _readHead = (_readHead + data.Count) % _buffer.Length;
        }
        else
        {
            // copy the data out of the buffer
            // ReSharper disable once AssignNullToNotNullAttribute (Justification: Array segment cannot be null)
            Array.Copy(_buffer, _readHead, data.Array, data.Offset, data.Count);
            _readHead += data.Count;
        }

#pragma warning disable 420
        // Justification: It's Interlocked, so volatile isn't important (See: http://stackoverflow.com/a/425150/108234 )
        Interlocked.Add(ref _unread, -data.Count);
#pragma warning restore 420

        return true;
    }

    #endregion

    public void Clear()
    {
        _readHead = 0;
        _writeHead = 0;
        _unread = 0;
    }
}