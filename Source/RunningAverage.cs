using System;

namespace AchtungMod;

public class RunningAverage<T> where T : struct, IConvertible
{
    private readonly int capacity;
    private readonly T[] samples;
    private int index;
    private double sum;
    private bool filled;

    public RunningAverage(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        this.capacity = capacity;
        samples = new T[capacity];
        index = 0;
        sum = 0.0;
        filled = false;
    }

    public T Add(T value)
    {
        var oldValue = Convert.ToDouble(samples[index]);
        sum -= oldValue;
        samples[index] = value;
        sum += Convert.ToDouble(value);
        index = (index + 1) % capacity;
        if (index == 0) filled = true;
        var count = filled ? capacity : index;
        var average = sum / count;
        return (T)Convert.ChangeType(average, typeof(T));
    }
}