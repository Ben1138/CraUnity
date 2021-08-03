using UnityEngine;
using Unity.Collections;

public class CraDataContainer<T> where T : struct
{
    NativeArray<T> Elements;
    int Head;

    ~CraDataContainer()
    {
        Destroy();
    }

    public CraDataContainer(int capacity)
    {
        Elements = new NativeArray<T>(capacity, Allocator.Persistent);
    }

    public NativeArray<T> GetMemoryBuffer()
    {
        Debug.Assert(Elements.IsCreated);
        return Elements;
    }

    public int GetCapacity()
    {
        return Elements.Length;
    }

    public int GetNumAllocated()
    {
        return Head;
    }

    // returns index
    public int Alloc()
    {
        Debug.Assert(Elements.IsCreated);
        if (Head == Elements.Length)
        {
            Debug.LogError($"Max capacity of {Elements.Length} reached!");
            return -1;
        }
        return Head++;
    }

    public bool Alloc(int count)
    {
        Debug.Assert(count > 0);

        int space = Elements.Length - (Head + count);
        if (space < 0)
        {
            Debug.LogError($"Alloc {count} elements exceeds the capacity of {Elements.Length}!");
            return false;
        }
        Head += count;
        return true;
    }

    public T Get(int index)
    {
        Debug.Assert(Elements.IsCreated);
        Debug.Assert(index >= 0 && index < Head);
        return Elements[index];
    }

    public void Set(int index, in T value)
    {
        Debug.Assert(Elements.IsCreated);
        Debug.Assert(index >= 0 && index < Head);
        Elements[index] = value;
    }

    public bool AllocFrom(T[] buffer)
    {
        Debug.Assert(buffer != null);
        Debug.Assert(buffer.Length > 0);

        int previousHead = Head;
        if (!Alloc(buffer.Length))
        {
            return false;
        }
        NativeArray<T>.Copy(buffer, 0, Elements, previousHead, buffer.Length);
        return true;
    }

    public void Clear()
    {
        Head = 0;
    }

    public void Destroy()
    {
        Head = 0;
        Elements.Dispose();
    }
}

public class CraDataContainerManaged<T> where T : struct
{
    T[] Elements;
    int Head;

    ~CraDataContainerManaged()
    {
        Destroy();
    }

    public CraDataContainerManaged(int capacity)
    {
        Elements = new T[capacity];
    }

    public int GetCapacity()
    {
        return Elements.Length;
    }

    public int GetNumAllocated()
    {
        return Head;
    }

    // returns index
    public int Alloc()
    {
        if (Head == Elements.Length)
        {
            Debug.LogError($"Max capacity of {Elements.Length} reached!");
            return -1;
        }
        return Head++;
    }

    public bool Alloc(int count)
    {
        Debug.Assert(count > 0);

        int space = Elements.Length - (Head + count);
        if (space < 0)
        {
            Debug.LogError($"Alloc {count} elements exceeds the capacity of {Elements.Length}!");
            return false;
        }
        Head += count;
        return true;
    }

    public ref T Get(int index)
    {
        Debug.Assert(index >= 0 && index < Head);
        return ref Elements[index];
    }

    public void Clear()
    {
        Head = 0;
    }

    public void Destroy()
    {
        Head = 0;
        Elements = null;
    }
}