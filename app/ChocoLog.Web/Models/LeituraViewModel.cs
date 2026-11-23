using System;

namespace ChocoLog.Web.Models
{
    // uma medida (ou um alerta) guardado no STH-Comet
    public class LeituraViewModel
    {
        public string Grandeza { get; set; }
        public DateTime Data { get; set; }
        public string Valor { get; set; }

        public double? ValorNumerico
        {
            get
            {
                double v;
                if (double.TryParse(Valor, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
                    return v;
                return null;
            }
        }
    }
}
