using System.Collections.Generic;
using Objects.Other;
using Speckle.Newtonsoft.Json;

namespace Objects.Geometry;

public class ControlPoint : Point, ITransformable<ControlPoint>
{
  public ControlPoint() { }

  public ControlPoint(double x, double y, double z, string units, string applicationId = null)
    : base(x, y, z, units, applicationId)
  {
    weight = 1;
  }

  public ControlPoint(double x, double y, double z, double w, string units, string applicationId = null)
    : base(x, y, z, units, applicationId)
  {
    weight = w;
  }

  /// <summary>
  /// OBSOLETE - This is just here for backwards compatibility.
  /// You should not use this for anything. Access coordinates using X,Y,Z and weight fields.
  /// </summary>
  [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
  private List<double> value
  {
    get => null;
    set
    {
      x = value[0];
      y = value[1];
      z = value[2];
      weight = value.Count > 3 ? value[3] : 1;
    }
  }

  public double weight { get; set; }

  public bool TransformTo(Transform transform, out ControlPoint ctrlPt)
  {
    TransformTo(transform, out Point transformedPoint);
    ctrlPt = new ControlPoint(transformedPoint.x, transformedPoint.y, transformedPoint.z, weight, units, applicationId);
    return true;
  }

  public override string ToString()
  {
    return $"{{{x},{y},{z},{weight}}}";
  }

  public void Deconstruct(out double x, out double y, out double z, out double weight)
  {
    Deconstruct(out x, out y, out z, out weight, out _);
  }

  public void Deconstruct(out double x, out double y, out double z, out double weight, out string units)
  {
    Deconstruct(out x, out y, out z, out units);
    weight = this.weight;
  }
}
