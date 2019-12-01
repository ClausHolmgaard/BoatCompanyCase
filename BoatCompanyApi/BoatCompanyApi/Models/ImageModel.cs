using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BoatCompanyApi.Models
{
    public class ImageModel
    {
        public string Uri { get; set; }
        public int numObjects { get; set; }
        public List<string> descriptions { get; set; }
    }
}
