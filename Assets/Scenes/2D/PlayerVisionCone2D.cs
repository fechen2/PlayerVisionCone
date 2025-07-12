#nullable enable
using System;
using Unity.Collections;
using UnityEngine;

namespace Game
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlayerVisionCone2D : MonoBehaviour, IDisposable
    {
        [SerializeField] private LayerMask _visionEnvLayerMask = default; // 环境的LayerMask
        [SerializeField] private LayerMask _visionTargetLayerMask = default; // 目标的LayerMask
        [SerializeField] private bool _open;
        [SerializeField] private float _visionConeAngle = 60.0f; //视椎角度
        [SerializeField] private float _visionConeRange = 10.0f; //视椎范围
        [SerializeField] private float _visionPerceptiveRadius = 2.0f; //圆的半径
        [SerializeField] private float _visionSegmentAngle = 10.0f; //射线间隔角度

        private Mesh _mesh = null!;
        private MeshRenderer _meshRenderer = null!;
        private Vector3[] _localVertices = null!;
        private NativeArray<int> _triangles;
        private int _pointAmount;

        private void Awake()
        {
            _mesh = new Mesh();
            GetComponent<MeshFilter>().mesh = _mesh;
            _meshRenderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            _pointAmount = Mathf.FloorToInt(360.0f / _visionSegmentAngle) + 4; //额外多添加4个顶点，多几条线没关系
            _localVertices = new Vector3[3 * _pointAmount];
            _triangles = new NativeArray<int>(_localVertices.Length * 3, Allocator.Persistent);
        }

        protected void FixedUpdate()
        {
            transform.localPosition = Vector3.zero;
            //clear VisionTargetTag first
            foreach (TargetVisionTag targetVisionTag in GameObject.FindObjectsOfType<TargetVisionTag>())
            {
                targetVisionTag.ShowVision = false;
            }
            //

            _meshRenderer.enabled = _open;
            if (_open)
            {
                float coneAngle = _visionConeAngle;
                float coneRange = _visionConeRange;
                float radius = _visionPerceptiveRadius;
                float segmentAngle = _visionSegmentAngle;
                Debug.Assert(coneRange > radius); //这里要求视椎的范围大于圆的半径，才有意义

                NativeArray<Vector3> tempLocalVertices = new NativeArray<Vector3>(_pointAmount - 1, Allocator.TempJob);
                NativeArray<bool> tempIsHitEnv = new NativeArray<bool>(_pointAmount - 1, Allocator.TempJob);
                Vector3 centerPosition = transform.position;

                #region 创建Cone的顶点数

                int realVerticesIndex = 0;
                int coneLineAmount = Mathf.FloorToInt(coneAngle / segmentAngle) + 1;
                for (int i = 0; i < coneLineAmount; i++)
                {
                    float radian = (i != coneLineAmount - 1)
                        ? (coneAngle * -0.5f + i * segmentAngle) * Mathf.Deg2Rad
                        : coneAngle * 0.5f * Mathf.Deg2Rad;

                    tempLocalVertices[realVerticesIndex] = new Vector3(
                        Mathf.Cos(radian) * coneRange,
                        Mathf.Sin(radian) * coneRange,
                        0
                    );
                    realVerticesIndex++;
                }

                #endregion

                Vector3 coneStartDirection = transform.TransformDirection(tempLocalVertices[0]); //TODO: performance
                Vector3 coneEndDirection = transform.TransformDirection(tempLocalVertices[realVerticesIndex - 1]);

                #region 创建圆的顶点数

                int circleLineAmount = _pointAmount - 1 - realVerticesIndex;
                for (int i = 0; i < circleLineAmount; i++)
                {
                    //Even thought a few more vertices, we can set the same position for them...
                    float nextAngle = Mathf.Min(coneAngle * 0.5f + i * segmentAngle, 360.0f - coneAngle * 0.5f);
                    float radian = nextAngle * Mathf.Deg2Rad;
                    tempLocalVertices[realVerticesIndex] = new Vector3(
                        Mathf.Cos(radian) * radius,
                        Mathf.Sin(radian) * radius,
                        0
                    );
                    realVerticesIndex++;
                }

                #endregion

                //----------check env, maybe change vertex position-----------

                #region 检测2D环境，刷新顶点数据

                using PooledArray<RaycastHit2D> rayCastHits = new PooledArray<RaycastHit2D>(4);

                for (int i = 0; i < _pointAmount - 1; i++)
                {
                    int size = Physics2D.RaycastNonAlloc( //使用2D射线检测环境
                        origin: centerPosition,
                        direction: transform.TransformDirection(tempLocalVertices[i]).normalized,
                        distance: tempLocalVertices[i].magnitude, //注意：这里要保证parent没有scale，否则要重新计算世界坐标系下的长度
                        results: rayCastHits.GetValue(),
                        layerMask: _visionEnvLayerMask);

                    if (size == 0) continue;

                    float nearestDistance = float.MaxValue;
                    for (int j = 0; j < size; j++)
                    {
                        RaycastHit2D hit = rayCastHits[j];
                        if (hit.collider != null && nearestDistance > hit.distance)
                        {
                            nearestDistance = hit.distance;
                        }
                    }

                    if (!Mathf.Approximately(nearestDistance, float.MaxValue))
                    {
                        tempLocalVertices[i] = tempLocalVertices[i].normalized * nearestDistance;
                        tempIsHitEnv[i] = true;
                    }
                }

                #endregion

                //---------------check target------------------
                using PooledArray<Collider2D> overlapSphereTargetResults = new PooledArray<Collider2D>(32);
                int count = Physics2D.OverlapCircleNonAlloc(
                    point: centerPosition,
                    radius: coneRange, //线检测大视椎范围内的enemy
                    results: overlapSphereTargetResults.GetValue(), //获取可能要显示的目标
                    layerMask: _visionTargetLayerMask);

                using PooledArray<RaycastHit2D> targetRaycastHits = new PooledArray<RaycastHit2D>(4);
                //对每个可能的目标进行2个细节筛选 1.环境遮挡不显示 2.不在小圆内并且角度不在 cone范围内不显示
                for (int i = 0; i < count; i++)
                {
                    Collider2D collider = overlapSphereTargetResults[i];
                    Vector3 targetColliderPosition = collider.transform.position;
                    Vector3 direction = targetColliderPosition - centerPosition;
                    int envRaycastCount = Physics2D.RaycastNonAlloc(
                        origin: centerPosition,
                        direction: direction.normalized,
                        distance: direction.magnitude,
                        results: targetRaycastHits,
                        layerMask: _visionEnvLayerMask);

                    if (envRaycastCount != 0) continue; //如果enemy被环境遮挡了，直接不显示

                    bool inCircle = Vector3.Distance(targetColliderPosition, centerPosition) <= (radius + ColliderHalfSize(collider));

                    if (!inCircle)
                    {
                        Vector2 direction2D = new Vector2(direction.x, direction.y);
                        Vector2 startDirection = new Vector2(coneStartDirection.x, coneStartDirection.y);
                        Vector2 endDirection = new Vector2(coneEndDirection.x, coneEndDirection.y);

                        float flag = Mathf.Sign(Vector2.SignedAngle(startDirection, endDirection));
                        float v1 = Mathf.Sign(Vector2.SignedAngle(startDirection, direction2D));
                        float v2 = Mathf.Sign(Vector2.SignedAngle(direction2D, endDirection));
                        if (flag != v1 || flag != v2)
                        {
                            //如果不在小圆内，而且角度不在椎体范围，不显示
                            continue;
                        }
                    }

                    //TODO:performance:
                    //通过collider找到target并且刷新标记
                    TargetVisionTag targetVisionTag = collider.GetComponentInParent<TargetVisionTag>();
                    if (targetVisionTag != null)
                    {
                        targetVisionTag.ShowVision = true;
                    }
                }

                int verticesIndex = 0;
                _localVertices[verticesIndex++] = Vector3.zero;
                for (int i = 0; i < tempLocalVertices.Length - 1; i++)
                {
                    bool isHitEnv = tempIsHitEnv[i];
                    bool isNextHitEnv = tempIsHitEnv[i + 1];
                    _localVertices[verticesIndex++] = tempLocalVertices[i];
                    if (isHitEnv == isNextHitEnv) continue;
                    //add extra 2 vertices between hit/noHit points to fix edge aliasing
                    //如果当前射线和下一射线中有一个是HitEnv的，我们在中间再添2条线，来修复边缘锯齿
                    Vector3 position = tempLocalVertices[i];
                    Vector3 nextPosition = tempLocalVertices[i + 1];
                    Vector3 extraDirection = ((position + nextPosition) * 0.5f).normalized;
                    Vector3 newPosition = position.magnitude * extraDirection;
                    Vector3 nextNewPosition = nextPosition.magnitude * extraDirection;
                    _localVertices[verticesIndex++] = newPosition;
                    _localVertices[verticesIndex++] = nextNewPosition;
                }

                tempLocalVertices.Dispose();
                tempIsHitEnv.Dispose();

#if UNITY_EDITOR
                _testGizmoAmount = verticesIndex;
#endif
                //triangles
                for (int i = 0; i < verticesIndex; i++)
                {
                    _triangles[i * 3] = 0;
                    _triangles[i * 3 + 1] = i;
                    _triangles[i * 3 + 2] = (i + 1) % verticesIndex;
                }

                //渲染这个mesh
                _mesh.vertices = _localVertices;
                //_mesh.triangles = _triangles;
                _mesh.SetIndices(indices: _triangles, topology: MeshTopology.Triangles, submesh: 0);
                _mesh.RecalculateNormals();
            }
        }

#if UNITY_EDITOR
        private int _testGizmoAmount = 0;

        private void OnDrawGizmos()
        {
            if (_localVertices == null) return;
            Gizmos.color = Color.green;
            for (int i = 0; i < _testGizmoAmount; i++)
            {
                Gizmos.DrawLine(transform.position, transform.TransformPoint(_localVertices[i]));
                Gizmos.DrawSphere(transform.TransformPoint(_localVertices[i]), 0.05f);
            }
        }
#endif
        public void Dispose()
        {
            _triangles.Dispose();
        }

        //需要扩展常见的类型
        private static float ColliderHalfSize(Collider2D collider2D)
        {
            if (collider2D is BoxCollider2D boxCollider2D)
            {
                return boxCollider2D.size.magnitude * 0.5f;
            }
            else if (collider2D is CircleCollider2D circleCollider2D)
            {
                return circleCollider2D.radius;
            }

            Debug.LogError("  Collider type not supported: " + collider2D.GetType());
            return 0;
        }
    }
}
