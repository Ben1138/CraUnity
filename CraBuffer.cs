using UnityEngine;
using Unity.Collections;



//public class CraDataContainerManaged<T> where T : struct
//{
//    T[] Elements;
//    int Head;

//    ~CraDataContainerManaged()
//    {
//        Destroy();
//    }

//    public CraDataContainerManaged(int capacity)
//    {
//        Elements = new T[capacity];
//    }

//    public int GetCapacity()
//    {
//        return Elements.Length;
//    }

//    public int GetNumAllocated()
//    {
//        return Head;
//    }

//    // returns index
//    public int Alloc()
//    {
//        if (Head == Elements.Length)
//        {
//            Debug.LogError($"Max capacity of {Elements.Length} reached!");
//            return -1;
//        }
//        return Head++;
//    }

//    public bool Alloc(int count)
//    {
//        Debug.Assert(count > 0);

//        int space = Elements.Length - (Head + count);
//        if (space < 0)
//        {
//            Debug.LogError($"Alloc {count} elements exceeds the capacity of {Elements.Length}!");
//            return false;
//        }
//        Head += count;
//        return true;
//    }

//    public ref T Get(int index)
//    {
//        Debug.Assert(index >= 0 && index < Head);
//        return ref Elements[index];
//    }

//    public void Clear()
//    {
//        Head = 0;
//    }

//    public void Destroy()
//    {
//        Head = 0;
//        Elements = null;
//    }
//}