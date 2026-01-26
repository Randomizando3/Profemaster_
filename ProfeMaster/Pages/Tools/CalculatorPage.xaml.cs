// Pages/Tools/CalculatorPage.xaml.cs
namespace ProfeMaster.Pages.Tools;

public partial class CalculatorPage : ContentPage
{
    private string _expr = "";

    public CalculatorPage()
    {
        InitializeComponent();
        RefreshUi();
    }

    private async void OnBack(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private void OnAppend(object sender, EventArgs e)
    {
        if (sender is not Button b) return;
        var t = (b.Text ?? "").Trim();
        if (string.IsNullOrEmpty(t)) return;

        // map símbolos bonitos para operadores
        t = t.Replace("÷", "/").Replace("×", "*").Replace("?", "-");

        _expr += t;
        RefreshUi(liveEvaluate: true);
    }

    private void OnBackspace(object sender, EventArgs e)
    {
        if (_expr.Length > 0)
            _expr = _expr[..^1];

        RefreshUi(liveEvaluate: true);
    }

    private void OnClear(object sender, EventArgs e)
    {
        _expr = "";
        RefreshUi();
    }

    private void OnEquals(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_expr))
        {
            ResultLabel.Text = "0";
            return;
        }

        try
        {
            var val = Eval(_expr);
            ResultLabel.Text = FormatNumber(val);
            // opcional: mantém expressão como resultado
            _expr = ResultLabel.Text == "Erro" ? _expr : ResultLabel.Text;
            ExprLabel.Text = "";
        }
        catch
        {
            ResultLabel.Text = "Erro";
        }
    }

    private void RefreshUi(bool liveEvaluate = false)
    {
        ExprLabel.Text = _expr;

        if (!liveEvaluate || string.IsNullOrWhiteSpace(_expr))
        {
            if (string.IsNullOrWhiteSpace(_expr))
                ResultLabel.Text = "0";
            return;
        }

        try
        {
            var val = Eval(_expr);
            ResultLabel.Text = FormatNumber(val);
        }
        catch
        {
            // Não grita erro enquanto digita; deixa o último ok
        }
    }

    private static string FormatNumber(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "Erro";

        // mostra inteiro sem .0
        var rounded = Math.Round(v, 10);
        if (Math.Abs(rounded - Math.Round(rounded)) < 1e-10)
            return ((long)Math.Round(rounded)).ToString();

        return rounded.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ======== Parser simples: + - * / e parênteses =========
    private static double Eval(string input)
    {
        var s = input.Replace(" ", "");
        var p = new Parser(s);
        var v = p.ParseExpression();
        p.ExpectEnd();
        return v;
    }

    private sealed class Parser
    {
        private readonly string _s;
        private int _i;

        public Parser(string s) { _s = s; _i = 0; }

        public void ExpectEnd()
        {
            if (_i < _s.Length)
                throw new Exception("Trailing");
        }

        public double ParseExpression()
        {
            var v = ParseTerm();
            while (true)
            {
                if (Match('+')) v += ParseTerm();
                else if (Match('-')) v -= ParseTerm();
                else break;
            }
            return v;
        }

        private double ParseTerm()
        {
            var v = ParseFactor();
            while (true)
            {
                if (Match('*')) v *= ParseFactor();
                else if (Match('/')) v /= ParseFactor();
                else break;
            }
            return v;
        }

        private double ParseFactor()
        {
            if (Match('+')) return ParseFactor();
            if (Match('-')) return -ParseFactor();

            if (Match('('))
            {
                var v = ParseExpression();
                if (!Match(')')) throw new Exception("Missing )");
                return v;
            }

            return ParseNumber();
        }

        private double ParseNumber()
        {
            if (_i >= _s.Length) throw new Exception("Expected number");

            var start = _i;
            var hasDot = false;

            while (_i < _s.Length)
            {
                var c = _s[_i];
                if (char.IsDigit(c)) { _i++; continue; }
                if (c == '.')
                {
                    if (hasDot) break;
                    hasDot = true;
                    _i++;
                    continue;
                }
                break;
            }

            if (_i == start) throw new Exception("Expected number");

            var token = _s.Substring(start, _i - start);
            if (!double.TryParse(token, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
                throw new Exception("Bad number");

            return v;
        }

        private bool Match(char c)
        {
            if (_i < _s.Length && _s[_i] == c)
            {
                _i++;
                return true;
            }
            return false;
        }
    }
}
