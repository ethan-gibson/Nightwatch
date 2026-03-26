using System.Collections.Generic;
using UnityEngine;

namespace Game.Entities.Octree
{
	public class Octree
	{
		private const float faceKeyScale = 1000f;
		private const float neighbourSkin = 0.05f;
		private const float minimumOverlapPadding = 0.01f;

		private readonly Bounds rootBounds;
		private readonly int maxDepth;
		private readonly float minLeafSize;
		private readonly float agentRadius;
		private readonly float agentHeight;
		private readonly LayerMask obstacleMask;
		private readonly LayerMask groundMask;
		private readonly float groundCheckDistance;
		private readonly Collider[] overlapBuffer = new Collider[64];
		private readonly List<OctreeNode> allLeaves = new List<OctreeNode>();
		private readonly List<OctreeNode> freeLeaves = new List<OctreeNode>();

		private OctreeNode root;

		private readonly struct Range
		{
			public readonly float Min;
			public readonly float Max;

			public Range(float _min, float _max)
			{
				Min = _min;
				Max = _max;
			}
		}

		private readonly struct FaceInfo
		{
			public readonly OctreeNode Node;
			public readonly int Axis;
			public readonly bool IsPositiveFace;
			public readonly Range PrimaryRange;
			public readonly Range SecondaryRange;

			public FaceInfo(OctreeNode _node, int _axis, bool _isPositiveFace, Range _primaryRange, Range _secondaryRange)
			{
				Node = _node;
				Axis = _axis;
				IsPositiveFace = _isPositiveFace;
				PrimaryRange = _primaryRange;
				SecondaryRange = _secondaryRange;
			}
		}

		private sealed class FaceBucket
		{
			public readonly List<FaceInfo> PositiveFaces = new List<FaceInfo>();
			public readonly List<FaceInfo> NegativeFaces = new List<FaceInfo>();
		}

		public Octree(Bounds _bounds, int _maxDepth, float _minLeafSize, float _agentRadius, float _agentHeight, LayerMask _obstacleMask, LayerMask _groundMask, float _groundCheckDistance)
		{
			rootBounds = _bounds;
			maxDepth = _maxDepth;
			minLeafSize = _minLeafSize;
			agentRadius = _agentRadius;
			agentHeight = Mathf.Max(_agentHeight, _agentRadius * 2f);
			obstacleMask = _obstacleMask;
			groundMask = _groundMask;
			groundCheckDistance = _groundCheckDistance;

			rebuild();
		}

		public IReadOnlyList<OctreeNode> GetAllFreeLeaves()
		{
			return freeLeaves;
		}

		public IReadOnlyList<OctreeNode> GetAllLeaves()
		{
			return allLeaves;
		}

		public OctreeNode FindLeafAtPoint(Vector3 _point)
		{
			if (root == null || !root.Bounds.Contains(_point)) { return null; }
			return findLeafAtPointRecursive(root, _point);
		}

		public OctreeNode FindNearestFreeLeaf(Vector3 _point, float _maxDistance)
		{
			float _bestDistanceSqr = _maxDistance * _maxDistance;
			OctreeNode _bestNode = null;

			foreach (OctreeNode _node in freeLeaves)
			{
				float _distanceSqr = _node.TraversalBounds.SqrDistance(_point);
				if (_distanceSqr > _bestDistanceSqr) { continue; }

				_bestDistanceSqr = _distanceSqr;
				_bestNode = _node;
			}

			return _bestNode;
		}

		public OctreeNode FindNearestConnectedFreeLeaf(Vector3 _point, float _maxDistance)
		{
			float _bestDistanceSqr = _maxDistance * _maxDistance;
			OctreeNode _bestNode = null;

			foreach (OctreeNode _node in freeLeaves)
			{
				if (_node.Neighbors.Count == 0) { continue; }

				float _distanceSqr = _node.TraversalBounds.SqrDistance(_point);
				if (_distanceSqr > _bestDistanceSqr) { continue; }

				_bestDistanceSqr = _distanceSqr;
				_bestNode = _node;
			}

			return _bestNode;
		}

		public bool HasClearPath(Vector3 _start, Vector3 _end)
		{
			Vector3 _direction = _end - _start;
			float _distance = _direction.magnitude;
			if (_distance <= minimumOverlapPadding) { return isCapsuleClear(_start); }
			if (!isCapsuleClear(_start) || !isCapsuleClear(_end)) { return false; }

			getCapsulePoints(_start, out Vector3 _topPoint, out Vector3 _bottomPoint);
			float _castRadius = Mathf.Max(agentRadius - neighbourSkin, minimumOverlapPadding);
			return !Physics.CapsuleCast(_topPoint, _bottomPoint, _castRadius, _direction / _distance, _distance, obstacleMask, QueryTriggerInteraction.Ignore);
		}

		public void RebuildRegion(Bounds _dirtyBounds)
		{
			if (root == null)
			{
				rebuild();
				return;
			}

			Vector3 _padding = Vector3.one * Mathf.Max(agentRadius * 2f, minLeafSize);
			Bounds _expandedBounds = _dirtyBounds;
			_expandedBounds.Expand(_padding);

			if (!rootBounds.Intersects(_expandedBounds)) { return; }

			rebuildRegionRecursive(root, _expandedBounds, 0);
			refreshGraph();
		}

		private void rebuild()
		{
			root = new OctreeNode(rootBounds);
			buildRecursive(root, 0);
			refreshGraph();
		}

		private void buildRecursive(OctreeNode _node, int _depth)
		{
			_node.Neighbors.Clear();
			_node.HasGround = false;
			_node.SamplePoint = _node.Bounds.center;
			_node.TraversalBounds = _node.Bounds;

			if (_depth >= maxDepth || hasReachedMinLeafSize(_node.Bounds))
			{
				finalizeLeaf(_node);
				return;
			}

			if (!containsObstaclePressure(_node.Bounds))
			{
				finalizeLeaf(_node);
				return;
			}

			_node.IsLeaf = false;
			_node.Children = new OctreeNode[8];

			Vector3 _center = _node.Bounds.center;
			Vector3 _childExtents = _node.Bounds.extents * 0.5f;
			Vector3 _childSize = _node.Bounds.size * 0.5f;

			for (int _i = 0; _i < 8; _i++)
			{
				Vector3 _childCenter = _center + new Vector3(
					(_i & 1) == 0 ? -_childExtents.x : _childExtents.x,
					(_i & 2) == 0 ? -_childExtents.y : _childExtents.y,
					(_i & 4) == 0 ? -_childExtents.z : _childExtents.z
				);

				OctreeNode _childNode = new OctreeNode(new Bounds(_childCenter, _childSize));
				_node.Children[_i] = _childNode;
				buildRecursive(_childNode, _depth + 1);
			}

			bool _allChildrenOccupied = true;
			for (int _i = 0; _i < _node.Children.Length; _i++)
			{
				if (_node.Children[_i].IsOccupied) { continue; }
				_allChildrenOccupied = false;
				break;
			}

			_node.IsOccupied = _allChildrenOccupied;
		}

		private void finalizeLeaf(OctreeNode _node)
		{
			_node.IsLeaf = true;
			_node.Children = null;

			if (!tryResolveSamplePoint(_node.Bounds, out Vector3 _samplePoint))
			{
				_node.IsOccupied = true;
				_node.HasGround = false;
				return;
			}

			_node.HasGround = true;
			_node.SamplePoint = _samplePoint;
			_node.TraversalBounds = createTraversalBounds(_node.Bounds, _samplePoint);
			_node.IsOccupied = !isCapsuleClear(_samplePoint);
		}

		private bool containsObstaclePressure(Bounds _bounds)
		{
			Vector3 _halfExtents = _bounds.extents + Vector3.one * (agentRadius + neighbourSkin);
			int _hitCount = Physics.OverlapBoxNonAlloc(_bounds.center, _halfExtents, overlapBuffer, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
			return _hitCount > 0;
		}

		private bool tryResolveSamplePoint(Bounds _bounds, out Vector3 _samplePoint)
		{
			Vector3 _rayStart = new Vector3(_bounds.center.x, _bounds.max.y + 0.25f, _bounds.center.z);
			float _rayDistance = groundCheckDistance + _bounds.size.y + agentHeight;

			if (!Physics.Raycast(_rayStart, Vector3.down, out RaycastHit _hit, _rayDistance, groundMask, QueryTriggerInteraction.Ignore))
			{
				_samplePoint = _bounds.center;
				return false;
			}

			_samplePoint = _hit.point + Vector3.up * (agentHeight * 0.5f);

			// Reject if the ground hit is below this leaf's floor level.
			// A small tolerance (agentRadius) allows for mesh thickness.
			// This prevents phantom nodes where a cross-floor raycast finds a
			// lower floor's surface and places the SamplePoint inside the floor mesh.
			if (_hit.point.y < _bounds.min.y - agentRadius)
			{
				_samplePoint = _bounds.center;
				return false;
			}

			return true;
		}

		private Bounds createTraversalBounds(Bounds _bounds, Vector3 _samplePoint)
		{
			Vector3 _size = _bounds.size;
			_size.y = Mathf.Max(_size.y, agentHeight);
			return new Bounds(_samplePoint, _size);
		}

		private bool isCapsuleClear(Vector3 _centerPoint)
		{
			getCapsulePoints(_centerPoint, out Vector3 _topPoint, out Vector3 _bottomPoint);
			float _checkRadius = Mathf.Max(agentRadius - neighbourSkin, minimumOverlapPadding);
			return !Physics.CheckCapsule(_topPoint, _bottomPoint, _checkRadius, obstacleMask, QueryTriggerInteraction.Ignore);
		}

		private void getCapsulePoints(Vector3 _centerPoint, out Vector3 _topPoint, out Vector3 _bottomPoint)
		{
			float _halfHeight = agentHeight * 0.5f;
			float _segmentOffset = Mathf.Max(0f, _halfHeight - agentRadius);
			Vector3 _offset = Vector3.up * _segmentOffset;
			_topPoint = _centerPoint + _offset;
			_bottomPoint = _centerPoint - _offset;
		}

		private bool hasReachedMinLeafSize(Bounds _bounds)
		{
			float _minimumAxis = Mathf.Min(_bounds.size.x, Mathf.Min(_bounds.size.y, _bounds.size.z));
			return _minimumAxis <= minLeafSize;
		}

		private void rebuildRegionRecursive(OctreeNode _node, Bounds _dirtyBounds, int _depth)
		{
			if (!_node.Bounds.Intersects(_dirtyBounds)) { return; }

			if (_node.IsLeaf || _node.Children == null || containsBounds(_dirtyBounds, _node.Bounds))
			{
				buildRecursive(_node, _depth);
				return;
			}

			for (int _i = 0; _i < _node.Children.Length; _i++)
			{
				rebuildRegionRecursive(_node.Children[_i], _dirtyBounds, _depth + 1);
			}

			bool _allChildrenOccupied = true;
			for (int _i = 0; _i < _node.Children.Length; _i++)
			{
				if (_node.Children[_i].IsOccupied) { continue; }
				_allChildrenOccupied = false;
				break;
			}

			_node.IsLeaf = false;
			_node.IsOccupied = _allChildrenOccupied;
		}

		private bool containsBounds(Bounds _outerBounds, Bounds _innerBounds)
		{
			return _outerBounds.min.x <= _innerBounds.min.x
				&& _outerBounds.min.y <= _innerBounds.min.y
				&& _outerBounds.min.z <= _innerBounds.min.z
				&& _outerBounds.max.x >= _innerBounds.max.x
				&& _outerBounds.max.y >= _innerBounds.max.y
				&& _outerBounds.max.z >= _innerBounds.max.z;
		}

		private void refreshGraph()
		{
			allLeaves.Clear();
			freeLeaves.Clear();
			gatherLeaves(root, allLeaves);

			for (int _i = 0; _i < allLeaves.Count; _i++)
			{
				OctreeNode _node = allLeaves[_i];
				_node.Neighbors.Clear();
				if (_node.IsOccupied) { continue; }
				freeLeaves.Add(_node);
			}

			connectNeighbors();
		}

		private void gatherLeaves(OctreeNode _node, List<OctreeNode> _leaves)
		{
			if (_node == null) { return; }

			if (_node.IsLeaf)
			{
				_leaves.Add(_node);
				return;
			}

			if (_node.Children == null) { return; }

			for (int _i = 0; _i < _node.Children.Length; _i++)
			{
				gatherLeaves(_node.Children[_i], _leaves);
			}
		}

		private void connectNeighbors()
		{
			Dictionary<long, FaceBucket> _faceBuckets = new Dictionary<long, FaceBucket>();

			for (int _i = 0; _i < freeLeaves.Count; _i++)
			{
				addNodeFaces(_faceBuckets, freeLeaves[_i]);
			}

			foreach (FaceBucket _bucket in _faceBuckets.Values)
			{
				for (int _positiveIndex = 0; _positiveIndex < _bucket.PositiveFaces.Count; _positiveIndex++)
				{
					FaceInfo _positiveFace = _bucket.PositiveFaces[_positiveIndex];
					for (int _negativeIndex = 0; _negativeIndex < _bucket.NegativeFaces.Count; _negativeIndex++)
					{
						FaceInfo _negativeFace = _bucket.NegativeFaces[_negativeIndex];
						tryConnectNeighbors(_positiveFace, _negativeFace);
					}
				}
			}
		}

		private void addNodeFaces(Dictionary<long, FaceBucket> _faceBuckets, OctreeNode _node)
		{
			addFace(_faceBuckets, _node, 0, false);
			addFace(_faceBuckets, _node, 0, true);
			addFace(_faceBuckets, _node, 1, false);
			addFace(_faceBuckets, _node, 1, true);
			addFace(_faceBuckets, _node, 2, false);
			addFace(_faceBuckets, _node, 2, true);
		}

		private void addFace(Dictionary<long, FaceBucket> _faceBuckets, OctreeNode _node, int _axis, bool _isPositiveFace)
		{
			float _plane = getPlane(_node.Bounds, _axis, _isPositiveFace);
			FaceInfo _face = createFaceInfo(_node, _axis, _isPositiveFace);
			long _key = getFaceKey(_axis, _plane);

			if (!_faceBuckets.TryGetValue(_key, out FaceBucket _bucket))
			{
				_bucket = new FaceBucket();
				_faceBuckets[_key] = _bucket;
			}

			if (_isPositiveFace) { _bucket.PositiveFaces.Add(_face); }
			else { _bucket.NegativeFaces.Add(_face); }
		}

		private FaceInfo createFaceInfo(OctreeNode _node, int _axis, bool _isPositiveFace)
		{
			Bounds _bounds = _node.TraversalBounds;

			switch (_axis)
			{
				case 0:
					return new FaceInfo(_node, _axis, _isPositiveFace, new Range(_bounds.min.y, _bounds.max.y), new Range(_bounds.min.z, _bounds.max.z));
				case 1:
					return new FaceInfo(_node, _axis, _isPositiveFace, new Range(_bounds.min.x, _bounds.max.x), new Range(_bounds.min.z, _bounds.max.z));
				default:
					return new FaceInfo(_node, _axis, _isPositiveFace, new Range(_bounds.min.x, _bounds.max.x), new Range(_bounds.min.y, _bounds.max.y));
			}
		}

		private float getPlane(Bounds _bounds, int _axis, bool _isPositiveFace)
		{
			switch (_axis)
			{
				case 0:
					return _isPositiveFace ? _bounds.max.x : _bounds.min.x;
				case 1:
					return _isPositiveFace ? _bounds.max.y : _bounds.min.y;
				default:
					return _isPositiveFace ? _bounds.max.z : _bounds.min.z;
			}
		}

		private long getFaceKey(int _axis, float _plane)
		{
			int _quantizedPlane = Mathf.RoundToInt(_plane * faceKeyScale);
			return ((long)_axis << 32) ^ (uint)_quantizedPlane;
		}

		private void tryConnectNeighbors(FaceInfo _positiveFace, FaceInfo _negativeFace)
		{
			if (_positiveFace.Node == _negativeFace.Node) { return; }
			if (_positiveFace.Axis != _negativeFace.Axis) { return; }
			if (_positiveFace.IsPositiveFace == _negativeFace.IsPositiveFace) { return; }
			if (!hasRequiredOverlap(_positiveFace, _negativeFace)) { return; }
			if (!HasClearPath(_positiveFace.Node.SamplePoint, _negativeFace.Node.SamplePoint)) { return; }

			addNeighbor(_positiveFace.Node, _negativeFace.Node);
			addNeighbor(_negativeFace.Node, _positiveFace.Node);
		}

		private bool hasRequiredOverlap(FaceInfo _firstFace, FaceInfo _secondFace)
		{
			float _primaryOverlap = getRangeOverlap(_firstFace.PrimaryRange, _secondFace.PrimaryRange);
			float _secondaryOverlap = getRangeOverlap(_firstFace.SecondaryRange, _secondFace.SecondaryRange);

			switch (_firstFace.Axis)
			{
				case 0:
					return _primaryOverlap >= getVerticalOverlapThreshold() && _secondaryOverlap >= getHorizontalOverlapThreshold();
				case 1:
					return _primaryOverlap >= getHorizontalOverlapThreshold() && _secondaryOverlap >= getHorizontalOverlapThreshold();
				default:
					return _primaryOverlap >= getHorizontalOverlapThreshold() && _secondaryOverlap >= getVerticalOverlapThreshold();
			}
		}

		private float getHorizontalOverlapThreshold()
		{
			// Only needs to prevent corner-touching false connections.
			// HasClearPath (capsule cast) is what enforces agent width clearance.
			return minLeafSize + minimumOverlapPadding;
		}

		private float getVerticalOverlapThreshold()
		{
			return Mathf.Max(agentHeight * 0.5f, minLeafSize + minimumOverlapPadding);
		}

		private float getRangeOverlap(Range _firstRange, Range _secondRange)
		{
			return Mathf.Min(_firstRange.Max, _secondRange.Max) - Mathf.Max(_firstRange.Min, _secondRange.Min);
		}

		private void addNeighbor(OctreeNode _node, OctreeNode _neighbor)
		{
			if (_node.Neighbors.Contains(_neighbor)) { return; }
			_node.Neighbors.Add(_neighbor);
		}

		private OctreeNode findLeafAtPointRecursive(OctreeNode _node, Vector3 _point)
		{
			if (!_node.Bounds.Contains(_point)) { return null; }
			if (_node.IsLeaf) { return _node; }
			if (_node.Children == null) { return null; }

			for (int _i = 0; _i < _node.Children.Length; _i++)
			{
				OctreeNode _result = findLeafAtPointRecursive(_node.Children[_i], _point);
				if (_result != null) { return _result; }
			}

			return null;
		}
	}
}
