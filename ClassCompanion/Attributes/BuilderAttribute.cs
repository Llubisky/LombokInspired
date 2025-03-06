using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassCompanion
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class BuilderAttribute: Attribute
    {
        /// <summary>
        /// If true it will generate a ToBuilder method in the main class.
        /// </summary>
        public bool ToBuilder { get; set; }

        /// <summary>
        /// If true it will include properties in the builder.
        /// </summary>
        public bool IncludeProperties { get; set; } = false;

        /// <summary>
        /// Prefix for the builder class methods.
        /// </summary>
        public string BuilderMethodPrefix { get; set; } = "Set";

        public BuilderAttribute(bool toBuilder = false)
        {
            ToBuilder = toBuilder;
        }
    }
}
