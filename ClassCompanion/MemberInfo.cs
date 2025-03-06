using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassCompanion
{
    // Clase para representar la información de un miembro (campo o propiedad)
    public class MemberInfo
    {
        public string Name { get; set; }
        public ITypeSymbol Type { get; set; }
    }
}
