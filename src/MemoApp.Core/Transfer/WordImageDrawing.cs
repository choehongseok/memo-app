using System.Globalization;
using System.Xml;
namespace MemoApp.Core.Transfer;
internal static class WordImageDrawing
{
    private const string W="http://schemas.openxmlformats.org/wordprocessingml/2006/main",WP="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing",A="http://schemas.openxmlformats.org/drawingml/2006/main",P="http://schemas.openxmlformats.org/drawingml/2006/picture",R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal static (long Width,long Height) Extent(int width,int height)
    {
        if(width is <1 or >4096||height is <1 or >4096||(long)width*height>4194304)throw new InvalidDataException("Word image geometry");
        long x=checked(width*9525L),y=checked(height*9525L),maxX=9026*635L,maxY=13958*635L;
        if(x>maxX||y>maxY){if(checked(x*maxY)>=checked(y*maxX)){y=Math.Max(1,checked(y*maxX)/x);x=maxX;}else{x=Math.Max(1,checked(x*maxY)/y);y=maxY;}}
        return(x,y);
    }
    internal static void Write(XmlWriter writer,int id,string relationship,int width,int height,string alt)
    {
        if(id is <1 or >102400)throw new InvalidDataException("Word drawing placement bound");XmlConvert.VerifyXmlChars(alt);var(x,y)=Extent(width,height);
        void Start(string prefix,string name,string ns)=>writer.WriteStartElement(prefix,name,ns);
        void Attr(string name,long value)=>writer.WriteAttributeString(name,value.ToString(CultureInfo.InvariantCulture));
        Start("w","p",W);Start("w","r",W);Start("w","drawing",W);Start("wp","inline",WP);
        foreach(string name in new[]{"distT","distB","distL","distR"})Attr(name,0);
        Start("wp","extent",WP);Attr("cx",x);Attr("cy",y);writer.WriteEndElement();
        Start("wp","docPr",WP);Attr("id",id);writer.WriteAttributeString("name","Image"+id.ToString("D6",CultureInfo.InvariantCulture));writer.WriteAttributeString("descr",alt);writer.WriteEndElement();
        Start("wp","cNvGraphicFramePr",WP);Start("a","graphicFrameLocks",A);writer.WriteAttributeString("noChangeAspect","1");writer.WriteEndElement();writer.WriteEndElement();
        Start("a","graphic",A);Start("a","graphicData",A);writer.WriteAttributeString("uri",P);Start("pic","pic",P);
        Start("pic","nvPicPr",P);Start("pic","cNvPr",P);Attr("id",id);writer.WriteAttributeString("name","Image"+id.ToString("D6",CultureInfo.InvariantCulture));writer.WriteEndElement();Start("pic","cNvPicPr",P);writer.WriteEndElement();writer.WriteEndElement();
        Start("pic","blipFill",P);Start("a","blip",A);writer.WriteAttributeString("r","embed",R,relationship);writer.WriteEndElement();Start("a","stretch",A);Start("a","fillRect",A);writer.WriteEndElement();writer.WriteEndElement();writer.WriteEndElement();
        Start("pic","spPr",P);Start("a","xfrm",A);Start("a","off",A);Attr("x",0);Attr("y",0);writer.WriteEndElement();Start("a","ext",A);Attr("cx",x);Attr("cy",y);writer.WriteEndElement();writer.WriteEndElement();Start("a","prstGeom",A);writer.WriteAttributeString("prst","rect");Start("a","avLst",A);writer.WriteEndElement();writer.WriteEndElement();writer.WriteEndElement();
        for(int i=0;i<7;i++)writer.WriteEndElement();
    }
    internal static void Section(XmlWriter writer)
    {
        writer.WriteStartElement("w","sectPr",W);writer.WriteStartElement("w","pgSz",W);writer.WriteAttributeString("w","w",W,"11906");writer.WriteAttributeString("w","h",W,"16838");writer.WriteEndElement();
        writer.WriteStartElement("w","pgMar",W);foreach(string name in new[]{"top","right","bottom","left"})writer.WriteAttributeString("w",name,W,"1440");writer.WriteAttributeString("w","header",W,"720");writer.WriteAttributeString("w","footer",W,"720");writer.WriteAttributeString("w","gutter",W,"0");writer.WriteEndElement();writer.WriteEndElement();
    }
}
