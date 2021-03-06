﻿using System;
using System.Collections.Generic;
using System.Text;

namespace Wabbajack.Common
{
    public static class ListExt
    {
        public static void SetTo<T>(this List<T> list, IEnumerable<T> rhs)
        {
            list.Clear();
            list.AddRange(rhs);
        }
    }
}
