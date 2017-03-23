namespace SignalRest.TestServer
{
    public class MathHub : Hub
    {
        public double Add(double a, double b)
        {
            return a + b;
        }

        public double AddThreeThings(double a, double b, double c)
        {
            return a + b + c;
        }

        public double Multiply(double a, double b)
        {
            return a * b;
        }

        public int GetStringLengths(string a, string b, string c)
        {
            return a.Length + b.Length + c.Length;
        }
    }
}
