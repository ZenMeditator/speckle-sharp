using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DB = Autodesk.Revit.DB;
using Mesh = Objects.Geometry.Mesh;

namespace Objects.Converter.Revit
{
  public partial class ConverterRevit
  {
    public Options SolidDisplayValueOptions = new Options()
    {
      DetailLevel = ViewDetailLevel.Fine,
      ComputeReferences = true
    };

    public Options ViewSpecificOptions { get; set; }

    /// <summary>
    /// We're caching a dictionary of graphic styles and their ids as it can be a costly operation doing Document.GetElement(solid.GraphicsStyleId) for every solid
    /// </summary>
    private Dictionary<string, GraphicsStyle> _graphicStyleCache = new Dictionary<string, GraphicsStyle>();

    /// <summary>
    /// Retreives the meshes on an element to use as the speckle displayvalue
    /// </summary>
    /// <param name="element"></param>
    /// <param name="isConvertedAsInstance">Some FamilyInstance elements are treated as proper Instance objects, while others are not. For those being converted as Instance objects, retrieve their display value untransformed by the instance transform or by the selected document reference point.</param>
    /// <returns></returns>
    /// <remarks>
    /// See https://www.revitapidocs.com/2023/e0f15010-0e19-6216-e2f0-ab7978145daa.htm for a full Geometry Object inheritance
    /// </remarks>
    public List<Mesh> GetElementDisplayValue(
      DB.Element element,
      Options options = null,
      bool isConvertedAsInstance = false,
      bool hasModifiedInstanceGeometry = false
    )
    {
      var displayMeshes = new List<Mesh>();

      // test if the element is a group first
      if (element is Group g)
      {
        foreach (var id in g.GetMemberIds())
        {
          var groupMeshes = GetElementDisplayValue(
            element.Document.GetElement(id),
            options,
            isConvertedAsInstance,
            hasModifiedInstanceGeometry
          );
          displayMeshes.AddRange(groupMeshes);
        }
        return displayMeshes;
      }

      options = ViewSpecificOptions ?? options ?? new Options();

      GeometryElement geom = null;
      try
      {
        geom = element.get_Geometry(options);
      }
      catch (Autodesk.Revit.Exceptions.ArgumentException)
      {
        options.ComputeReferences = false;
        geom = element.get_Geometry(options);
      }

      if (geom == null)
        return displayMeshes;

      // retrieves all meshes and solids from a geometry element
      var solids = new List<Solid>();
      var meshes = new List<DB.Mesh>();
      SortGeometry(geom);
      void SortGeometry(GeometryElement geom, Transform inverseTransform = null)
      {
        foreach (GeometryObject geomObj in geom)
        {
          switch (geomObj)
          {
            case Solid solid:
              // skip invalid solid
              if (solid.Faces.Size == 0 || Math.Abs(solid.SurfaceArea) == 0)
                break;

              if (!IsSkippableGraphicStyle(solid.GraphicsStyleId, element.Document))
              {
                if (inverseTransform != null)
                  solid = SolidUtils.CreateTransformed(solid, inverseTransform);

                solids.Add(solid);
              }
              break;
            case DB.Mesh mesh:
              if (!IsSkippableGraphicStyle(mesh.GraphicsStyleId, element.Document))
              {
                if (inverseTransform != null)
                  mesh = mesh.get_Transformed(inverseTransform);

                meshes.Add(mesh);
              }
              break;
            case GeometryInstance instance:
              var instanceGeo =
                isConvertedAsInstance && !hasModifiedInstanceGeometry
                  ? instance.GetSymbolGeometry()
                  : instance.GetInstanceGeometry();
              inverseTransform =
                isConvertedAsInstance && hasModifiedInstanceGeometry ? instance.Transform.Inverse : null;
              SortGeometry(instanceGeo, inverseTransform);
              break;
            case GeometryElement element:
              SortGeometry(element);
              break;
          }
        }
      }

      // convert meshes and solids
      displayMeshes.AddRange(ConvertMeshesByRenderMaterial(meshes, element.Document, isConvertedAsInstance));
      displayMeshes.AddRange(ConvertSolidsByRenderMaterial(solids, element.Document, isConvertedAsInstance));

      return displayMeshes;
    }

    /// <summary>
    /// Exclude light source cones and potentially other geometries by their graphic style
    /// </summary>
    /// <param name="id"></param>
    /// <param name="doc"></param>
    /// <returns></returns>
    private bool IsSkippableGraphicStyle(ElementId id, Document doc)
    {
      if (!_graphicStyleCache.ContainsKey(id.ToString()))
        _graphicStyleCache.Add(id.ToString(), doc.GetElement(id) as GraphicsStyle);
      var graphicStyle = _graphicStyleCache[id.ToString()];

      if (
        graphicStyle != null
        && graphicStyle.GraphicsStyleCategory.Id.IntegerValue == (int)(BuiltInCategory.OST_LightingFixtureSource)
      )
        return true;
      return false;
    }

    /// <summary>
    /// Given a collection of <paramref name="meshes"/>, will create one <see cref="Mesh"/> per distinct <see cref="DB.Material"/>
    /// </summary>
    /// <param name="meshes"></param>
    /// <param name="d"></param>
    /// <returns></returns>
    private List<Mesh> ConvertMeshesByRenderMaterial(
      List<DB.Mesh> meshes,
      Document d,
      bool doNotTransformWithReferencePoint = false
    )
    {
      MeshBuildHelper buildHelper = new MeshBuildHelper();

      foreach (var mesh in meshes)
      {
        var revitMaterial = d.GetElement(mesh.MaterialElementId) as DB.Material;
        Mesh speckleMesh = buildHelper.GetOrCreateMesh(revitMaterial, ModelUnits);
        ConvertMeshData(mesh, speckleMesh.faces, speckleMesh.vertices, d, doNotTransformWithReferencePoint);
      }

      return buildHelper.GetAllValidMeshes();
    }

    /// <summary>
    /// Given a collection of <paramref name="solids"/>, will create one <see cref="Mesh"/> per distinct <see cref="DB.Material"/>
    /// </summary>
    /// <param name="solids"></param>
    /// <param name="d"></param>
    /// <returns></returns>
    private List<Mesh> ConvertSolidsByRenderMaterial(
      IEnumerable<Solid> solids,
      Document d,
      bool doNotTransformWithReferencePoint = false
    )
    {
      MeshBuildHelper meshBuildHelper = new MeshBuildHelper();

      var MeshMap = new Dictionary<Mesh, List<DB.Mesh>>();
      foreach (Solid solid in solids)
      {
        foreach (Face face in solid.Faces)
        {
          DB.Material faceMaterial = d.GetElement(face.MaterialElementId) as DB.Material;
          Mesh m = meshBuildHelper.GetOrCreateMesh(faceMaterial, ModelUnits);
          if (!MeshMap.ContainsKey(m))
          {
            MeshMap.Add(m, new List<DB.Mesh>());
          }
          MeshMap[m].Add(face.Triangulate());
        }
      }

      foreach (var meshData in MeshMap)
      {
        //It's cheaper to resize lists manually, since we would otherwise be resizing a lot!
        int numberOfVertices = 0;
        int numberOfFaces = 0;
        foreach (DB.Mesh mesh in meshData.Value)
        {
          if (mesh == null)
            continue;
          numberOfVertices += mesh.Vertices.Count * 3;
          numberOfFaces += mesh.NumTriangles * 4;
        }

        meshData.Key.faces.Capacity = numberOfFaces;
        meshData.Key.vertices.Capacity = numberOfVertices;
        foreach (DB.Mesh mesh in meshData.Value)
        {
          if (mesh == null)
            continue;
          ConvertMeshData(mesh, meshData.Key.faces, meshData.Key.vertices, d, doNotTransformWithReferencePoint);
        }
      }

      return meshBuildHelper.GetAllValidMeshes();
    }

    /// <summary>
    /// Given <paramref name="mesh"/>, will convert and add triangle data to <paramref name="faces"/> and <paramref name="vertices"/>
    /// </summary>
    /// <param name="mesh">The revit mesh to convert</param>
    /// <param name="faces">The faces list to add to</param>
    /// <param name="vertices">The vertices list to add to</param>
    private void ConvertMeshData(
      DB.Mesh mesh,
      List<int> faces,
      List<double> vertices,
      Document doc,
      bool doNotTransformWithReferencePoint = false
    )
    {
      int faceIndexOffset = vertices.Count / 3;

      foreach (var vert in mesh.Vertices)
      {
        var (x, y, z) = PointToSpeckle(vert, doc, null, doNotTransformWithReferencePoint);
        vertices.Add(x);
        vertices.Add(y);
        vertices.Add(z);
      }

      for (int i = 0; i < mesh.NumTriangles; i++)
      {
        var triangle = mesh.get_Triangle(i);

        faces.Add(3); // TRIANGLE flag
        faces.Add((int)triangle.get_Index(0) + faceIndexOffset);
        faces.Add((int)triangle.get_Index(1) + faceIndexOffset);
        faces.Add((int)triangle.get_Index(2) + faceIndexOffset);
      }
    }
  }
}
