using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;


namespace GML_ISO_Converter
{
    // ISO 11783 output type
    public enum ISO_TYPE
    {
        PFD,            // partfield
        TZN,            // treatment zone
        TSK             // task
    }

    // template for process data value elements
    public class PDV_ELEMENTS
    {
        public PDV_ELEMENTS(string GML_element, XElement PDV_template, double scale)
        {
            this.GML_element = GML_element;
            this.PDV_template = PDV_template;
            this.scale = scale;
        }
        
        public string GML_element;
        public XElement PDV_template;
        public double scale;
    }

    class Point
    {
        public string Y;    // GPS position north
        public string X;    // GPS position east
    }

    // the main class
    class GML_ISO_Converter
    {
        // define namespaces in GML file
        XNamespace gml = "http://www.opengis.net/gml";
        XNamespace usr;
        string gmlcoordtype = "[lat_lon]";

        // source elements in GML file for PFD-type
        string elem_code;
        string elem_designator;

        // source elements in GML file for TZN-type
        List<PDV_ELEMENTS> elem_PDV = new List<PDV_ELEMENTS>();

        // additional arguments given by user for TZN/TSK-type
        List<XAttribute> TSK_Attributes = new List<XAttribute>();
        List<XElement> TSK_Elements = new List<XElement>();

        // parameters for TSK
        bool merge = false;
        bool shrink = false;

        // intput / output file names
        string inputfile = "input.gml";
        string outputfile;

        // conversion type
        ISO_TYPE outputtype = ISO_TYPE.PFD;


        // Executable entry point
        static void Main(string[] args)
        {
            GML_ISO_Converter program = new GML_ISO_Converter();

            // parse arguments
            if (!program.parseArgs(args))
                return;

            // check that all arguments are given
            if (!program.checkArgs())
                return;

            // run the conversion
            switch (program.outputtype)
            {
                case ISO_TYPE.PFD:
                    program.convert_PFD();
                    break;
                case ISO_TYPE.TZN:
                    program.convert_TZN();
                    break;
                case ISO_TYPE.TSK:
                    program.convert_TSK();
                    break;
            }
        }

        // parse arguments
        public bool parseArgs(string[] args)
        {
            foreach (string arg in args)
            {
                // arguments type is: "-argument=value" --> split to argument/value pairs
                string[] split = arg.Split(new Char[] { '=' });

                if (split.Length > 1)
                {
                    switch (split[0])
                    {
                        case "-input":
                            this.inputfile = split[1];
                            break;
                        case "-output":
                            this.outputfile = split[1];
                            break;
                        case "-type":
                            switch (split[1])
                            {
                                case "PFD":
                                    this.outputtype = ISO_TYPE.PFD;
                                    break;
                                case "TZN":
                                    this.outputtype = ISO_TYPE.TZN;
                                    break;
                                case "TSK":
                                    this.outputtype = ISO_TYPE.TSK;
                                    break;
                                default:
                                    Console.WriteLine("Unrecognized output type '" + split[1] + "'. Allowed options: PFD, TZN, TSK");
                                    return false;
                            }
                            break;
                        case "-namespace":
                            this.usr = split[1];
                            break;
                        case "-GMLnamespace":
                            this.gml = split[1];
                            break;
                        case "-GMLcoordinates":
                             this.gmlcoordtype = split[1];
                            break;
                        // options for PFD:
                        case "-PFD:B":
                            this.elem_code = split[1];
                            break;
                        case "-PFD:C":
                            this.elem_designator = split[1];
                            break;
                        // options for TZN:
                        case "-PDV":
                            // PDV argument value type is: "gml_element:{PDV_attribute:attribute_value, ...}" --> extract gml part
                            string gml_name = split[1].Substring(0, split[1].IndexOf(':'));

                            if (gml_name.Length == 0)
                            {
                                Console.WriteLine("Wrong syntax in PDV element name declaration: '" + split[1] + "'");
                                return false;
                            }

                            // search PDV part
                            string attributes = split[1].Substring(split[1].IndexOf('{') + 1);
                            attributes = attributes.Substring(0, attributes.IndexOf('}'));

                            // split to PDV attribute/value list
                            string[] attr_split = attributes.Split(new Char[] { ',' });

                            if (attr_split.Length == 0)
                            {
                                Console.WriteLine("Wrong syntax in PDV attribute declaration: '" + split[1] + "'");
                                return false;
                            }

                            // create a template PDV-element from attribute/value pairs
                            XElement PDV = new XElement("PDV");
                            double scale = 1.0;
                            foreach (string attr in attr_split)
                            {
                                // split to attribute/value pair
                                string[] attr_val = attr.Split(new Char[] { ':' });

                                if (attr_val.Length < 2)
                                {
                                    Console.WriteLine("Wrong syntax in PDV attribute declaration: '" + attr + "'");
                                    return false;
                                }

                                switch (attr_val[0])
                                {
                                    case "scale":
                                        scale = double.Parse(attr_val[1]);
                                        break;
                                    default:
                                        PDV.Add(new XAttribute(attr_val[0], attr_val[1]));
                                        break;
                                }
                            }

                            // insert new template to list
                            elem_PDV.Add(new PDV_ELEMENTS(gml_name, PDV, scale));
                            break;
                        // options for TZN / TSK
                        case "-ATR":
                            // additional agruments given by the user: "-ARG=<attribute>:<value>"
                            string[] arguments = split[1].Split(new Char[] { ':' });

                            if (arguments.Length < 2)
                            {
                                Console.WriteLine("Wrong syntax in ARG declaration: '" + split[1] + "'");
                                return false;
                            }

                            TSK_Attributes.Add(new XAttribute(arguments[0], arguments[1]));
                            break;
                        case "-ELM":
                            // Additional element for the TASK
                            // ELM argument value type is: "element:{attribute:attribute_value, ...}" 
                            string element_name = split[1].Substring(0, split[1].IndexOf(':'));

                            if (element_name.Length == 0)
                            {
                                Console.WriteLine("Wrong syntax in ELM element name declaration: '" + split[1] + "'");
                                return false;
                            }

                            // search attribute part
                            string xmlattributes = split[1].Substring(split[1].IndexOf('{') + 1);
                            xmlattributes = xmlattributes.Substring(0, xmlattributes.IndexOf('}'));

                            // split to attribute/value list
                            string[] xmlattr_split = xmlattributes.Split(new Char[] { ',' });

                            if (xmlattr_split.Length == 0)
                            {
                                Console.WriteLine("Wrong syntax in ELM attribute declaration: '" + split[1] + "'");
                                return false;
                            }

                            // create a template PDV-element from attribute/value pairs
                            XElement ELM = new XElement(element_name);
                            foreach (string attr in xmlattr_split)
                            {
                                // split to attribute/value pair
                                string[] attr_val = attr.Split(new Char[] { ':' });

                                if (attr_val.Length < 2)
                                {
                                    ELM.Add(attr);
                                }
                                else
                                {
                                    ELM.Add(new XAttribute(attr_val[0], attr_val[1]));
                                }
                            }

                            TSK_Elements.Add(ELM);
                            break;
                        // Options for TSK:
                        case "-merge":
                            switch (split[1])
                            {
                                case "true":
                                    merge = true;
                                    break;
                                case "false":
                                    merge = false;
                                    break;
                                default:
                                    Console.WriteLine("Options for merge is 'true' or 'false', input was: '" + split[1] + "'");
                                    return false;
                            }
                            break;
                        case "-shrink":
                            switch (split[1])
                            {
                                case "true":
                                    shrink = true;
                                    break;
                                case "false":
                                    shrink = false;
                                    break;
                                default:
                                    Console.WriteLine("Options for shrink is 'true' or 'false', input was: '" + split[1] + "'");
                                    return false;
                            }
                            break;
                        default:
                            Console.WriteLine("Unrecognized argument '" + split[0] + "'");
                            return false;
                    }
                }
            }

            return true;
        }

        // check that all arguments are set
        public bool checkArgs()
        {
            switch (outputtype)
            {
                case ISO_TYPE.PFD:
                    if (usr == null)
                    {
                        Console.WriteLine("GML namespace is not set");
                        return false;
                    }

                    if (elem_code == null)
                    {
                        Console.WriteLine("Partfield code element is not set");
                        return false;
                    }

                    if (elem_designator == null)
                    {
                        Console.WriteLine("Partfield designator element is not set");
                        return false;
                    }

                    if (outputfile == null)
                    {
                        // default output for partfield is:
                        outputfile = "PFD00001.xml";
                    }
                    break;

                case ISO_TYPE.TZN:
                    if (usr == null)
                    {
                        Console.WriteLine("GML namespace is not set");
                        return false;
                    }

                    if (elem_PDV.Count == 0)
                    {
                        Console.WriteLine("Zone prosess data variables are not set");
                        return false;
                    }

                    foreach (PDV_ELEMENTS PDV in elem_PDV)
                    {
                        if (PDV.PDV_template.Attribute("A") == null)
                        {
                            Console.WriteLine("Prosess data DDI is not set for element '" + PDV.GML_element + "'");
                            return false;
                        }

                        // attribute B will be read from GML file
                        if (PDV.PDV_template.Attribute("B") != null)
                        {
                            Console.WriteLine("Prosess data value cannot be set in declaration for element '" + PDV.GML_element + "'");
                            return false;
                        }
                    }

                    if (outputfile == null)
                    {
                        // default output for treatmentzone is:
                        outputfile = "TSK00001.XML";
                    }

                    break;

                case ISO_TYPE.TSK:

                    if (outputfile == null)
                    {
                        // default output for task is:
                        outputfile = "TASKDATA.XML";
                    }

                    break;
            }

            return true;
        }


        // The code for partfields
        public void convert_PFD()
        {
            // read GML file
            XDocument GMLFile;
            try
            {
                GMLFile = XDocument.Load(inputfile);
            }
            catch (System.IO.FileNotFoundException e)
            {
                Console.WriteLine(e.Message);
                return;
            }

            // create ISO 11783 external file contents
            XDocument ISOFile = new XDocument(new XElement("XFC"));

            // select zones from the GML
            var fields = GMLFile.Root.Descendants(gml + "featureMember");

            if (fields.Count() == 0)
                fields = GMLFile.Root.Descendants(usr + "featureMember");

            // loop through fields
            int count = 0;
            foreach (var field in fields)
            {
                // counter for partfield id
                count++;

                string info_to_exeption = "";    // hold information of current parser state
                try
                {
                    // read field code
                    info_to_exeption = "Exeption in reading the field code:";
                    var code = field
                        .Descendants(usr + elem_code)
                        .Select(e => e.Value)
                        .Single();

                    // read field designator
                    info_to_exeption = "Exeption in reading the field designator:";
                    var designator = field
                        .Descendants(usr + elem_designator)
                        .Select(e => e.Value)
                        .Single();

                    XElement PFD = new XElement("PFD",                      // Element PDF: Partfield
                        new XAttribute("A", "PFD" + count),                 // Attribute A: Partfield ID
                        new XAttribute("B", code),                          // Attribute B: PatrtfieldCode
                        new XAttribute("C", designator)                     // Attribute C: PartfielddDesignator
                    );

                    // total area for this partfield, calculated from boundaries
                    double total_area = 0;

                    // search outer boundaries for this treatment zone
                    var outerBoundaries = field.Descendants(gml + "outerBoundaryIs");
                    if (outerBoundaries.Count() == 0)
                        outerBoundaries = field.Descendants(gml + "exterior");    // "exterior" in GML 3.0

                    foreach (var outerBoundary in outerBoundaries)
                    {
                        // TBD: inner boundaries 
                        //var innerBoundaries = zone.Descendants(gml + "innerBoundaryIs");    // "interior" in GML 3.0

                        // read outer boundary coordinates
                        info_to_exeption = "Exeption in reading the field outer boundary:";
                        var GMLcoordinates = outerBoundary.Descendants(gml + "coordinates");

                        if (GMLcoordinates.Count() == 0)
                            GMLcoordinates = outerBoundary.Descendants(gml + "posList");

                        // "coordinates" includes ',' between latitude and longitude, whereas "posList" does not 
                        var coordinates = GMLcoordinates
                            .Select(y => y.Value.Split(',', ' ').ToArray())
                            .Single();

                        List<Point> boundary = new List<Point>();
                        for (int i=0; i<coordinates.Length; i+=2)
                        {
                            if(gmlcoordtype == "[lat_lon]")
                                boundary.Add(new Point { Y = coordinates[i], X = coordinates[i + 1] });
                            else // gmlcoordtype == "[lon_lat]")
                                boundary.Add(new Point { X = coordinates[i], Y = coordinates[i + 1] });
                        }

                        // calculate partfield area
                        var prev_point = boundary.Last();
                        double area = 0;
                        foreach (var point in boundary)
                        {
                            area += (Convert.ToDouble(point.X, CultureInfo.InvariantCulture) - Convert.ToDouble(prev_point.X)) * Math.PI / 180.0 *
                                    (2 + Math.Sin(Convert.ToDouble(point.Y) * Math.PI / 180.0) + Math.Sin(Convert.ToDouble(prev_point.Y) * Math.PI / 180.0));
                            prev_point = point;
                        }
                        area = Math.Round(area * 6378137.0 * 6378137.0 / 2.0);

                        if (area < 0)
                            area = -area;

                        total_area += area;

                        // convert coordinates to ISO 11783 partfield 
                        PFD.Add(
                            new XElement("PLN",                             // Element PLN: Polygon
                            new XAttribute("A","1"),                        // Attribute A: PolygonType: 1 = Partfield Boyndary
                                new XElement("LSG",                         // Element LSG: LineString
                                new XAttribute("A", "1"),                   // Attribute A: LineStringType: 1 = PolygonExterior, 2 = PolygonInterior
                                boundary.Select(point =>
                                    new XElement("PNT",                     // Element PNT: Point
                                        new XAttribute("A", "2"),           // Attribute A: PointType: 2 = Other
                                        new XAttribute("C", point.Y),       // Attribute C: GPS position north
                                        new XAttribute("D", point.X)        // Attribute D: GPS position east
                                        )
                                    )
                                )
                            )
                        );

                    }

                    // Add total area attribute
                    PFD.Add(new XAttribute("D", total_area));               // Attribute D: PartfieldArea

                    // create partfield element to task file
                    ISOFile.Root.Add(PFD);
                }
                catch (System.InvalidOperationException e)
                {
                    // write an explanatio to exeption if there were any
                    Console.WriteLine(info_to_exeption);
                    Console.WriteLine(e.Message);
                }

            }

            // save ISO 11783 external file
            ISOFile.Save(outputfile);
        }

        // The code for partfields
        public void convert_TZN()
        {
            // read GML file
            XDocument GMLFile;
            try
            {
                GMLFile = XDocument.Load(inputfile);
            }
            catch (System.IO.FileNotFoundException e)
            {
                Console.WriteLine(e.Message);
                return;
            }


            // create ISO 11783 task file
            XDocument ISOFile = new XDocument(
                new XElement("XFC",
                    new XElement("TSK", 
                            new XAttribute("A", "TSK1"),                                           // TaskId 
                            new XAttribute("G", "1")                                               // TaskStatus, 1 = planned
                    )
                )
            );

            // add attributes given by the user
            foreach(var attr in TSK_Attributes)
            {
                ISOFile.Root.Element("TSK").Add(attr);
            }

            // add elements given by the user
            foreach(var elm in TSK_Elements)
            {
                ISOFile.Root.Element("TSK").Add(elm);
            }
            
            

            // select zones from the GML
            var featureMembers = GMLFile.Root.Descendants(gml + "featureMember");

            if (featureMembers.Count() == 0)
                featureMembers = GMLFile.Root.Descendants(usr + "featureMember");

            // loop through zones
            int count = 0;
            foreach (var featureMember in featureMembers)
            {
                // counter for zone id
                count++;

                string info_to_exeption = "";    // hold information of current parser state
                try
                {
                    // actual zone is the first child element of featureMember element
                    info_to_exeption = "Exeption in reading the treatment zone content";
                    XElement zone = featureMember.Elements().First();

                    // search outer boundaries for this treatment zone
                    info_to_exeption = "Exeption in reading the treatment zone outer boundary:";
                    var outerBoundary = zone.Descendants(gml + "outerBoundaryIs").FirstOrDefault();

                    if(outerBoundary == null)
                        outerBoundary = zone.Descendants(gml + "exterior").FirstOrDefault();    // "exterior" in GML 3.0

                    if (outerBoundary == null)
                        continue;                   // this feature member is not a treatment zone

                    // create new treatment zone element
                    XElement TZN = new XElement("TZN",                  // Element TZN: TreatmentZone
                        new XAttribute("A",count.ToString()),           // Attribute A: TreatmentZoneCode
                        new XAttribute("B",zone.Name.LocalName)         // Attribute B: TreatmentZoneDesignator
                        );

                    // search process data values for this zone
                    foreach (PDV_ELEMENTS PDV in elem_PDV)
                    {
                        info_to_exeption = "Exeption in reading the process data value '" + PDV.GML_element + "'";

                        XElement gml_process_data = zone.Element(usr + PDV.GML_element);
                        if(gml_process_data != null)
                        {
                            // create new PDV from template
                            XElement newPDV = new XElement(PDV.PDV_template);
                            
                            // add process data value for this PDV
                            newPDV.Add(new XAttribute("B", double.Parse(gml_process_data.Value)*PDV.scale));

                            // add the new PDV for this treatment zone
                            TZN.Add(newPDV);
                        }
                    }

                    // read outer boundary coordinates
                    info_to_exeption = "Exeption in reading the treatment zone outer boundary coordinates:";
                    var GMLcoordinates = outerBoundary.Descendants(gml + "coordinates");

                    if(GMLcoordinates.Count() == 0)
                        GMLcoordinates = outerBoundary.Descendants(gml + "posList");

                    // "coordinates" includes ',' between latitude and longitude, whereas "posList" does not 
                    var coordinates = GMLcoordinates
                        .Select(y => y.Value.Split(',', ' ').ToArray())
                        .Single();

                    List<Point> boundary = new List<Point>();
                    for (int i = 0; i < coordinates.Length; i += 2)
                    {
                        if (gmlcoordtype == "[lat_lon]")
                            boundary.Add(new Point { Y = coordinates[i], X = coordinates[i + 1] });
                        else // gmlcoordtype == "[lon_lat]")
                            boundary.Add(new Point { X = coordinates[i], Y = coordinates[i + 1] });
                    }

                    // convert coordinates to ISO 11783 treatment zone polygon
                    XElement PLN = new XElement("PLN",                      // Element PLN: Polygon
                                        new XAttribute("A", "2")            // Attribute A: PolygonType: 2 = TreatmentZone 
                    );
                    TZN.Add(PLN);

                        
                    PLN.Add(new XElement("LSG",                             // Element LSG: LineString
                            new XAttribute("A", "1"),                       // Attribute A: LineStringType: 1 = PolygonExterior, 2 = PolygonInterior
                            boundary.Select(point =>
                                new XElement("PNT",                         // Element PNT: Point
                                    new XAttribute("A", "2"),               // Attribute A: PointType: 2 = Other
                                    new XAttribute("C", point.Y),           // Attribute C: GPS position north
                                    new XAttribute("D", point.X)            // Attribute D: GPS position east
                                )
                            )
                        )
                    );

                    info_to_exeption = "Exeption in reading the treatment zone inner boundary:";
                    var innerBoundaries = zone.Descendants(gml + "innerBoundaryIs");    
                    if (innerBoundaries == null)
                        innerBoundaries = zone.Descendants(gml + "interior");    // "interior" in GML 3.0


                    foreach(var innerBoundary in innerBoundaries)
                    {
                        GMLcoordinates = innerBoundary.Descendants(gml + "coordinates");

                        if (GMLcoordinates.Count() == 0)
                            GMLcoordinates = innerBoundary.Descendants(gml + "posList");

                        // "coordinates" includes ',' between latitude and longitude, whereas "posList" does not 
                        coordinates = GMLcoordinates
                            .Select(y => y.Value.Split(',', ' ').ToArray())
                            .Single();

                        boundary.Clear();
                        for (int i = 0; i < coordinates.Length; i += 2)
                        {
                            if (gmlcoordtype == "[lat_lon]")
                                boundary.Add(new Point { Y = coordinates[i], X = coordinates[i + 1] });
                            else // gmlcoordtype == "[lon_lat]")
                                boundary.Add(new Point { X = coordinates[i], Y = coordinates[i + 1] });
                        }

                        PLN.Add(new XElement("LSG",                             // Element LSG: LineString
                                new XAttribute("A", "2"),                       // Attribute A: LineStringType: 1 = PolygonExterior, 2 = PolygonInterior
                                boundary.Select(point =>
                                    new XElement("PNT",                         // Element PNT: Point
                                        new XAttribute("A", "2"),               // Attribute A: PointType: 2 = Other
                                        new XAttribute("C", point.Y),           // Attribute C: GPS position north
                                        new XAttribute("D", point.X)            // Attribute D: GPS position east
                                    )
                                )
                            )
                        );
                    }
                    

                    // add new treatment zone to task
                    ISOFile.Root.Element("TSK").Add(TZN);
                
                }
                catch (System.Exception e)
                {
                    // write an explanatio to exeption if there were any
                    Console.WriteLine(info_to_exeption);
                    Console.WriteLine(e.Message);
                }

            }

            // save ISO 11783 external file
            ISOFile.Save(outputfile);
        }

        public void convert_TSK()
        {
            // create ISO 11783 task file
            XDocument ISOFile = new XDocument(
                new XElement("ISO11783_TaskData",
                    new XAttribute("VersionMajor", "4"),
                    new XAttribute("VersionMinor", "0"),
                    new XAttribute("ManagementSoftwareManufacturer", "LUKE"),
                    new XAttribute("ManagementSoftwareVersion", "2016.11.4.0"),
                    new XAttribute("DataTransferOrigin", "1")
                )
            );

            // add attributes given by the user
            foreach (var attr in TSK_Attributes)
            {
                ISOFile.Root.Add(attr);
            }

            // add elements given by the user
            foreach (var elm in TSK_Elements)
            {
                ISOFile.Root.Add(elm);
            }


            // merge external file contents to the main file
            if(merge)
            {
                string directory = System.IO.Path.GetDirectoryName(outputfile) + "\\";

                var XFRlist = ISOFile.Root.Descendants("XFR");

                foreach(var XFR in XFRlist)
                {
                    string file = directory + XFR.Value;
                    
                    XDocument EXTFile;
                    try
                    {
                        EXTFile = XDocument.Load(file);
                    }
                    catch (System.IO.FileNotFoundException e)
                    {
                        Console.WriteLine(e.Message);
                        return;
                    }

                    ISOFile.Root.Add(EXTFile.Element("XFC").Elements());

                    System.IO.File.Delete(file);
                }

                ISOFile.Root.Descendants("XFR").Remove();
            }


            if(shrink)
            {
                string partfield = ISOFile.Root.Element("TSK").Attribute("E").Value;
                ISOFile.Root.Descendants("PFD").Where(pfd => pfd.Attribute("A").Value != partfield).Remove();
            }


            // save ISO 11783 task file
            ISOFile.Save(outputfile);
        }
    }
}
