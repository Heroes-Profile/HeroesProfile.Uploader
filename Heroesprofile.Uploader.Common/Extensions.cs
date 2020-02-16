using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Heroesprofile.Uploader.Common
{
    public static class Extensions
    {
        /// <summary>
        /// Executes specified delegate on all members of the collection
        /// </summary>
        public static void Map<T>(this IEnumerable<T> src, Action<T> action)
        {
            src.Select(q => { action(q); return 0; }).Count();
        }

        /// <summary>
        /// Does nothing. Avoids compiler warning about the lack of await
        /// </summary>
        public static void Forget(this Task task) { }
    }
}
