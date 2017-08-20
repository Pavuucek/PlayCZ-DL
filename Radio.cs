using System;
using System.Collections.Generic;
using System.Text;

namespace PlayCZ2TvHeadend
{
    public class Radio
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Shortcut { get; set; }
        public string LogoUrl { get; set; }
        public List<string> StreamList { get; set; }

    }
}
