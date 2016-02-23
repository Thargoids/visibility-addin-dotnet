﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.Analyst3D;
using ESRI.ArcGIS.Geometry;
using ArcMapAddinVisibility.Helpers;
using System.Collections;
using ESRI.ArcGIS.Display;

namespace ArcMapAddinVisibility.ViewModels
{
    public class LOSBaseViewModel : TabBaseViewModel
    {
        public LOSBaseViewModel()
        {
            ObserverOffset = 2.0;
            TargetOffset = 0.0;
            OffsetUnitType = DistanceTypes.Meters;

            ObserverPoints = new ObservableCollection<IPoint>();
            ToolMode = MapPointToolMode.Unknown;
            SurfaceLayerNames = new ObservableCollection<string>();
            SelectedSurfaceName = string.Empty;

            Mediator.Register(Constants.MAP_TOC_UPDATED, OnMapTocUpdated);

            DeletePointCommand = new RelayCommand(OnDeletePointCommand);

            GuidPointDictionary = new Dictionary<string, IPoint>();
        }

        #region Properties

        private double? observerOffset;
        public double? ObserverOffset 
        {
            get { return observerOffset; }
            set
            {
                observerOffset = value;
                RaisePropertyChanged(() => ObserverOffset);

                if (!observerOffset.HasValue)
                    throw new ArgumentException(Properties.Resources.AEInvalidInput);
            }
        }
        private double? targetOffset;
        public double? TargetOffset 
        {
            get { return targetOffset; } 
            set
            {
                targetOffset = value;
                RaisePropertyChanged(() => TargetOffset);

                if (!targetOffset.HasValue)
                    throw new ArgumentException(Properties.Resources.AEInvalidInput);
            }
        }

        internal MapPointToolMode ToolMode { get; set; }
        public ObservableCollection<IPoint> ObserverPoints { get; set; }
        public ObservableCollection<string> SurfaceLayerNames { get; set; }
        public string SelectedSurfaceName { get; set; }
        public DistanceTypes OffsetUnitType { get; set; }
        public Dictionary<string, IPoint> GuidPointDictionary { get; set; } 

        #endregion

        #region Commands

        public RelayCommand DeletePointCommand { get; set; }

        internal virtual void OnDeletePointCommand(object obj)
        {
            // remove observer points
            var items = obj as IList;
            var points = items.Cast<IPoint>().ToList();

            if (points == null)
                return;

            // temp list of point's graphic element's guids
            var guidList = new List<string>();

            foreach (var point in points)
            {
                ObserverPoints.Remove(point);

                // remove graphic
                var kvp = GuidPointDictionary.FirstOrDefault(i => i.Value == point);

                guidList.Add(kvp.Key);
            }

            RemoveGraphics(guidList);

            foreach (var guid in guidList)
            {
                if (GuidPointDictionary.ContainsKey(guid))
                    GuidPointDictionary.Remove(guid);
            }
        }

        #endregion

        #region Event handlers

        private void OnMapTocUpdated(object obj)
        {
            if (ArcMap.Document == null || ArcMap.Document.FocusMap == null)
                return;

            var map = ArcMap.Document.FocusMap;

            var tempName = SelectedSurfaceName;

            SurfaceLayerNames.Clear();
            foreach (var name in GetSurfaceNamesFromMap(map))
                SurfaceLayerNames.Add(name);
            if (SurfaceLayerNames.Contains(tempName))
                SelectedSurfaceName = tempName;
            else if (SurfaceLayerNames.Any())
                SelectedSurfaceName = SurfaceLayerNames[0];
            else
                SelectedSurfaceName = string.Empty;

            RaisePropertyChanged(() => SelectedSurfaceName);
        }

        /// <summary>
        /// Override this method to implement a "Mode" to separate the input of
        /// observer points and target points
        /// </summary>
        /// <param name="obj">ToolMode string from resource file</param>
        internal override void OnActivateTool(object obj)
        {
            var mode = obj.ToString();
            ToolMode = MapPointToolMode.Unknown;

            if (string.IsNullOrWhiteSpace(mode))
                return;

            if (mode == Properties.Resources.ToolModeObserver)
                ToolMode = MapPointToolMode.Observer;
            else if (mode == Properties.Resources.ToolModeTarget)
                ToolMode = MapPointToolMode.Target;

            base.OnActivateTool(obj);
        }

        /// <summary>
        /// Override this event to collect observer points based on tool mode
        /// </summary>
        /// <param name="obj"></param>
        internal override void OnNewMapPointEvent(object obj)
        {
            // lets test this out
            if (!IsActiveTab)
                return;

            var point = obj as IPoint;

            if (point == null)
                return;

            // ok, we have a point
            if (ToolMode == MapPointToolMode.Observer)
            {
                // in tool mode "Observer" we add observer points
                // otherwise ignore
                ObserverPoints.Insert(0, point);
                var color = new RgbColorClass() { Green = 255 } as IColor;
                var guid = AddGraphicToMap(point, color, true);
                UpdatePointDictionary(point, guid);
            }
        }

        #endregion

        /// <summary>
        /// Enumeration used to the different tool modes
        /// </summary>
        internal enum MapPointToolMode : int
        {
            Unknown = 0,
            Observer = 1,
            Target = 2
        }

        internal void UpdatePointDictionary(IPoint point, string guid)
        {
            if (!GuidPointDictionary.ContainsKey(guid))
                GuidPointDictionary.Add(guid, point);
        }

        /// <summary>
        /// Method to get a z offset distance in the correct units for the map
        /// </summary>
        /// <param name="map">IMap</param>
        /// <param name="offset">the input offset</param>
        /// <param name="zFactor">ISurface z factor</param>
        /// <param name="distanceType">the "from" distance unit type</param>
        /// <returns></returns>
        internal double GetOffsetInZUnits(IMap map, double offset, double zFactor, DistanceTypes distanceType)
        {
            if (map.SpatialReference == null)
                return offset;

            double offsetInMapUnits = 0.0;
            DistanceTypes distanceTo = DistanceTypes.Meters; // default to meters

            var pcs = map.SpatialReference as IProjectedCoordinateSystem;

            if (pcs != null)
            {
                // need to convert the offset from the input distance type to the spatial reference linear type
                // then apply the zFactor
                distanceTo = GetDistanceType(pcs.CoordinateUnit.FactoryCode);
            }

            offsetInMapUnits = GetDistanceFromTo(distanceType, distanceTo, offset);

            var result = offsetInMapUnits / zFactor;

            return result;
        }

        /// <summary>
        /// Method to get a ISurface from a map with layer name
        /// </summary>
        /// <param name="map">IMap that contains surface layer</param>
        /// <param name="name">Name of the layer that you are looking for</param>
        /// <returns>ISurface</returns>
        public ISurface GetSurfaceFromMapByName(IMap map, string name)
        {
            for (int x = 0; x < map.LayerCount; x++)
            {
                var layer = map.get_Layer(x);

                if (layer == null || layer.Name != name)
                    continue;

                var rasterLayer = layer as IRasterLayer;
                if (rasterLayer == null)
                {
                    var tin = layer as ITinLayer;
                    if (tin != null)
                        return tin.Dataset as ISurface;

                    continue;
                }

                var rs = new RasterSurfaceClass() as IRasterSurface;

                rs.PutRaster(rasterLayer.Raster, 0);

                var surface = rs as ISurface;
                if (surface != null)
                    return surface;
            }

            return null;
        }

        /// <summary>
        /// Method to get all the names of the raster/tin layers that support ISurface
        /// we use this method to populate a combobox for input selection of surface layer
        /// </summary>
        /// <param name="map">IMap</param>
        /// <returns></returns>
        public List<string> GetSurfaceNamesFromMap(IMap map)
        {
            var list = new List<string>();

            for (int x = 0; x < map.LayerCount; x++)
            {
                try
                {
                    var layer = map.get_Layer(x);

                    var rasterLayer = layer as IRasterLayer;
                    if (rasterLayer == null)
                    {
                        var tin = layer as ITinLayer;
                        if(tin == null)
                            continue;

                        list.Add(layer.Name);
                        continue;
                    }

                    var rs = new RasterSurfaceClass() as IRasterSurface;

                    rs.PutRaster(rasterLayer.Raster, 0);

                    var surface = rs as ISurface;
                    if (surface != null)
                        list.Add(layer.Name);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }

            return list;
        }

        /// <summary>
        /// Override to add aditional items in the class to reset tool
        /// </summary>
        /// <param name="toolReset"></param>
        internal override void Reset(bool toolReset)
        {
            base.Reset(toolReset);

            if (ArcMap.Document == null || ArcMap.Document.FocusMap == null)
                return;

            // reset surface names OC
            var names = GetSurfaceNamesFromMap(ArcMap.Document.FocusMap);

            SurfaceLayerNames.Clear();

            foreach (var name in names)
                SurfaceLayerNames.Add(name);

            if (SurfaceLayerNames.Any())
                SelectedSurfaceName = SurfaceLayerNames[0];

            RaisePropertyChanged(() => SelectedSurfaceName);

            // reset observer points
            ObserverPoints.Clear();

            ClearTempGraphics();

            GuidPointDictionary.Clear();
        }
    }
}