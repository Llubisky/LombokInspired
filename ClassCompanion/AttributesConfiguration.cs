using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassCompanion
{
    // Clase para almacenar la configuración de los atributos
    public class AttributesConfiguration
    {
        public bool AplicarAllArgs { get; set; }
        public bool AplicarBuilder { get; set; }
        public bool ToBuilder { get; set; }
        public bool IncludePropertiesAllArgs { get; set; }
        public bool IncludePropertiesBuilder { get; set; }
        public string BuilderMethodPrefix { get; set; }
    }
}
