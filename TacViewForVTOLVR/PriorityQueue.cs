using System;
using System.Runtime.CompilerServices;

namespace TacViewForVTOLVR
{
    public abstract class PriorityQueueNode<U> : IComparable<U>
    {
        internal int QueueIndex;

        public abstract int CompareTo(U other);
    }

    public sealed class PriorityQueue<T>
        where T : PriorityQueueNode<T>
    {
        private int count;
        private T[] elements;

        public PriorityQueue(int maxElements)
        {
            count = 0;
            elements = new T[maxElements + 1];
        }

        public int Count
        {
            get { return count; }
        }

        public int MaxSize
        {
            get { return elements.Length - 1; }
        }

        public T First
        {
            get
            {
                if (count <= 0)
                    throw new InvalidOperationException("Cannot call .First on an empty queue");
                return elements[1];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            Array.Clear(elements, 1, count);
            count = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(T element)
        {
            if (count >= MaxSize)
                throw new InvalidOperationException("Queue is full");

            count++;
            elements[count] = element;
            element.QueueIndex = count;
            CascadeUp(element);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CascadeUp(T element)
        {
            //aka Heapify-up
            int parent;
            if (element.QueueIndex > 1)
            {
                parent = element.QueueIndex >> 1;
                T parentElement = elements[parent];
                if (parentElement.CompareTo(element) >= 0)
                    return;

                //Element has lower priority value, so move parent down the heap to make room
                elements[element.QueueIndex] = parentElement;
                parentElement.QueueIndex = element.QueueIndex;

                element.QueueIndex = parent;
            }
            else
            {
                return;
            }

            while (parent > 1)
            {
                parent >>= 1;
                T parentElement = elements[parent];
                if (parentElement.CompareTo(element) >= 0)
                    break;

                //Element has lower priority value, so move parent down the heap to make room
                elements[element.QueueIndex] = parentElement;
                parentElement.QueueIndex = element.QueueIndex;

                element.QueueIndex = parent;
            }

            elements[element.QueueIndex] = element;
        }

        private void CascadeDown(T element)
        {
            //aka Heapify-down
            int finalQueueIndex = element.QueueIndex;
            int childLeftIndex = 2 * finalQueueIndex;

            // If leaf element, we're done
            if (childLeftIndex > count)
            {
                return;
            }

            // Check if the left-child is higher-priority than the current element
            int childRightIndex = childLeftIndex + 1;
            T childLeft = elements[childLeftIndex];
            if (childLeft.CompareTo(element) > 0)
            {
                // Check if there is a right child. If not, swap and finish.
                if (childRightIndex > count)
                {
                    element.QueueIndex = childLeftIndex;
                    childLeft.QueueIndex = finalQueueIndex;
                    elements[finalQueueIndex] = childLeft;
                    elements[childLeftIndex] = element;
                    return;
                }

                // Check if the left-child is higher-priority than the right-child
                T childRight = elements[childRightIndex];
                if (childLeft.CompareTo(childRight) > 0)
                {
                    // left is highest, move it up and continue
                    childLeft.QueueIndex = finalQueueIndex;
                    elements[finalQueueIndex] = childLeft;
                    finalQueueIndex = childLeftIndex;
                }
                else
                {
                    // right is even higher, move it up and continue
                    childRight.QueueIndex = finalQueueIndex;
                    elements[finalQueueIndex] = childRight;
                    finalQueueIndex = childRightIndex;
                }
            }
            // Not swapping with left-child, does right-child exist?
            else if (childRightIndex > count)
            {
                return;
            }
            else
            {
                // Check if the right-child is higher-priority than the current element
                T childRight = elements[childRightIndex];
                if (childRight.CompareTo(element) > 0)
                {
                    childRight.QueueIndex = finalQueueIndex;
                    elements[finalQueueIndex] = childRight;
                    finalQueueIndex = childRightIndex;
                }
                // Neither child is higher-priority than current, so finish and stop.
                else
                {
                    return;
                }
            }

            while (true)
            {
                childLeftIndex = 2 * finalQueueIndex;

                // If leaf element, we're done
                if (childLeftIndex > count)
                {
                    element.QueueIndex = finalQueueIndex;
                    elements[finalQueueIndex] = element;
                    break;
                }

                // Check if the left-child is higher-priority than the current element
                childRightIndex = childLeftIndex + 1;
                childLeft = elements[childLeftIndex];
                if (childLeft.CompareTo(element) > 0)
                {
                    // Check if there is a right child. If not, swap and finish.
                    if (childRightIndex > count)
                    {
                        element.QueueIndex = childLeftIndex;
                        childLeft.QueueIndex = finalQueueIndex;
                        elements[finalQueueIndex] = childLeft;
                        elements[childLeftIndex] = element;
                        break;
                    }

                    // Check if the left-child is higher-priority than the right-child
                    T childRight = elements[childRightIndex];
                    if (childLeft.CompareTo(childRight) > 0)
                    {
                        // left is highest, move it up and continue
                        childLeft.QueueIndex = finalQueueIndex;
                        elements[finalQueueIndex] = childLeft;
                        finalQueueIndex = childLeftIndex;
                    }
                    else
                    {
                        // right is even higher, move it up and continue
                        childRight.QueueIndex = finalQueueIndex;
                        elements[finalQueueIndex] = childRight;
                        finalQueueIndex = childRightIndex;
                    }
                }
                // Not swapping with left-child, does right-child exist?
                else if (childRightIndex > count)
                {
                    element.QueueIndex = finalQueueIndex;
                    elements[finalQueueIndex] = element;
                    break;
                }
                else
                {
                    // Check if the right-child is higher-priority than the current element
                    T childRight = elements[childRightIndex];
                    if (childRight.CompareTo(element) > 0)
                    {
                        childRight.QueueIndex = finalQueueIndex;
                        elements[finalQueueIndex] = childRight;
                        finalQueueIndex = childRightIndex;
                    }
                    // Neither child is higher-priority than current, so finish and stop.
                    else
                    {
                        element.QueueIndex = finalQueueIndex;
                        elements[finalQueueIndex] = element;
                        break;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Dequeue()
        {
            if (count <= 0)
                throw new InvalidOperationException("Cannot call .Dequeue() on an empty queue");

            T returnMe = elements[1];
            //If the element is already the last element, we can remove it immediately
            if (count == 1)
            {
                elements[1] = null;
                count = 0;
                return returnMe;
            }

            //Swap the element with the last element
            T formerLastElement = elements[count];
            elements[1] = formerLastElement;
            formerLastElement.QueueIndex = 1;
            elements[count] = null;
            count--;

            //Now bubble formerLastElement (which is no longer the last element) down
            CascadeDown(formerLastElement);
            return returnMe;
        }

        public void Resize(int maxElements)
        {
            T[] newArray = new T[maxElements + 1];
            int highestIndexToCopy = Math.Min(maxElements, count);
            Array.Copy(elements, newArray, highestIndexToCopy + 1);
            elements = newArray;
        }
    }
}