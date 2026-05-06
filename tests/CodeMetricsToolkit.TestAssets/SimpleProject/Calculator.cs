namespace SimpleProject;

public sealed class Calculator
{
    public Calculator(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public int LastResult { get; private set; }

    public int Add(int left, int right)
    {
        LastResult = left + right;
        return LastResult;
    }

    public bool TryDivide(int dividend, int divisor, out int result)
    {
        if (divisor == 0)
        {
            result = 0;
            return false;
        }

        result = dividend / divisor;
        LastResult = result;
        return true;
    }
}
