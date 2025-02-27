using System.Collections.Generic;

using Autodesk.Revit.DB;

using Speckle.Core.Models;

using Objects.BuiltElements.Revit;
using RevitElementType = Objects.BuiltElements.Revit.RevitElementType;
using System.Linq;

namespace Objects.Converter.Revit
{
  public partial class ConverterRevit
  {
    public RevitElement RevitElementToSpeckle(Element revitElement, out List<string> notes)
    {
      notes = new List<string>();
      var symbol = revitElement.Document.GetElement(revitElement.GetTypeId()) as FamilySymbol;

      RevitElement speckleElement = new RevitElement();
      if (symbol != null)
      {
        speckleElement.family = symbol.FamilyName;
        speckleElement.type = symbol.Name;
      }
      else
      {
        speckleElement.type = revitElement.Name;
      }

      var baseGeometry = LocationToSpeckle(revitElement);
      if (baseGeometry is Geometry.Point point)
        speckleElement["basePoint"] = point;
      else if (baseGeometry is Geometry.Line line)
        speckleElement["baseLine"] = line;

      speckleElement.category = revitElement.Category.Name;

      GetHostedElements(speckleElement, revitElement, out notes);
      
      var elements = (speckleElement["elements"] ?? @speckleElement["@elements"]) as List<Base>;
      elements ??= new List<Base>();

      // get the displayvalue of this revit element
      var displayValue = GetElementDisplayValue(revitElement, new Options() { DetailLevel = ViewDetailLevel.Fine });
      if (!displayValue.Any() && elements.Count == 0)
      {
        notes.Add("Not sending elements without display meshes");
        return null;
      }
      speckleElement.displayValue = displayValue;

      GetAllRevitParamsAndIds(speckleElement, revitElement);

      return speckleElement;
    }

    public RevitElementType ElementTypeToSpeckle(ElementType revitType)
    {
      var type = revitType.Name;
      var family = revitType.FamilyName;
      var category = revitType.Category.Name;
      RevitElementType speckleType = null;

      switch (revitType)
      {
        case FamilySymbol o:
          var symbolType = new RevitSymbolElementType() { type = type, family = family, category = category };
          symbolType.placementType = o.Family?.FamilyPlacementType.ToString();
          speckleType = symbolType;
          break;
        case MEPCurveType o:
          var mepType = new RevitMepElementType() { type = type, family = family, category = category };
          mepType.shape = o.Shape.ToString();
          speckleType = mepType;
          break;
        default:
          speckleType = new RevitElementType() { type = type, family = family, category = category };
          break;
      }

      GetAllRevitParamsAndIds(speckleType, revitType);

      return speckleType;
    }
  }
}
