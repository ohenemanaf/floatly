using System;
using System.Globalization;

namespace Floatly.Services;

// A tiny safe expression parser; does not execute scripts or evaluate dynamic code.
public static class CalculatorEngine
{
    public static double Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 200)
            throw new FormatException("Enter a calculation up to 200 characters.");
        var parser = new Parser(expression);
        double value = parser.Expression();
        parser.SkipSpaces();
        if (!parser.End) throw new FormatException("Unexpected character in expression.");
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArithmeticException("Result is not finite.");
        return value;
    }

    private sealed class Parser(string text)
    {
        private int _i;
        public bool End => _i >= text.Length;
        public void SkipSpaces() { while (!End && char.IsWhiteSpace(text[_i])) _i++; }
        private bool Eat(char symbol)
        {
            SkipSpaces();
            if (!End && text[_i] == symbol) { _i++; return true; }
            return false;
        }
        public double Expression()
        {
            double value = Term();
            while (true)
            {
                if (Eat('+')) value += Term();
                else if (Eat('-')) value -= Term();
                else return value;
            }
        }
        private double Term()
        {
            double value = Factor();
            while (true)
            {
                if (Eat('*')) value *= Factor();
                else if (Eat('/'))
                {
                    double divisor = Factor();
                    if (divisor == 0) throw new DivideByZeroException("Cannot divide by zero.");
                    value /= divisor;
                }
                else return value;
            }
        }
        private double Factor()
        {
            if (Eat('+')) return Factor();
            if (Eat('-')) return -Factor();
            if (Eat('('))
            {
                double value = Expression();
                if (!Eat(')')) throw new FormatException("Missing closing parenthesis.");
                return value;
            }
            SkipSpaces();
            int start = _i;
            while (!End && (char.IsDigit(text[_i]) || text[_i] == '.')) _i++;
            if (start == _i || !double.TryParse(text[start.._i], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double number))
                throw new FormatException("Expected a valid number.");
            return number;
        }
    }
}
