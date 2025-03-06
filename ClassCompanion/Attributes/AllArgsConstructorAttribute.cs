using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassCompanion
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class AllArgsConstructorAttribute: Attribute
    {
        /// <summary>
        /// Used to include properties in the constructor
        /// </summary>
        public bool IncludeProperties { get; set; } = false;
    }
}
