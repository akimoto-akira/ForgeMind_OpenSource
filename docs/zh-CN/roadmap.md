# ForgeMind 开发路线图

本路线图展示 ForgeMind 资产生成能力的开发进度，按领域与状态分组，随开发进展持续更新。数据来源：Step 3 素材快照（2026-11-03）。

## 总览

| 状态 | 数量 |
| --- | ---: |
| 已完成 | 99 |
| 开发中 | 7 |
| 计划中 | 47 |
| 合计 | 153 |

## 按领域浏览

| 领域 | 已完成 | 开发中 | 计划中 |
| --- | ---: | ---: | ---: |
| 蓝图 | 72 | 1 | 0 |
| 材质 | 27 | 5 | 29 |
| 其他 | 0 | 1 | 18 |

## 蓝图

### 已完成（72）

- 蓝图 Cast
- 蓝图 Ease
- 蓝图 EditableVar
- 蓝图 Inherit
- 蓝图 Interface
- 蓝图 SpawnActor
- 蓝图 Struct
- 蓝图 for 循环
- 蓝图 if 分支
- 蓝图 while 循环
- 蓝图 事件
- 蓝图 事件分发器
- 蓝图 关卡流送
- 蓝图 关卡蓝图
- 蓝图 函数
- 蓝图 函数库
- 蓝图 动态材质
- 蓝图 变量
- 蓝图 存档
- 蓝图 宏
- 蓝图 宏库
- 蓝图 定时器
- 蓝图 局部变量
- 蓝图 插值
- 蓝图 构造对象
- 蓝图 构造脚本
- 蓝图 父类调用
- 蓝图 绑定实例事件
- 蓝图 调试绘制
- 蓝图 选择节点
- 蓝图 ClassDefault
- 蓝图 Collision
- 蓝图 ContainerMapSet
- 蓝图 Curve
- 蓝图 Enum
- 蓝图 EnumForEach
- 蓝图 FlowControl
- 蓝图 Gate
- 蓝图 Particle
- 蓝图 SoftRef
- 蓝图 ArrayOps
- 蓝图 Bitmask
- 蓝图 DataAsset
- 蓝图 DataTable
- 蓝图 Delay
- 蓝图 DelegateBind
- 蓝图 Destroy
- 蓝图 Input
- 蓝图 LineTrace
- 蓝图 Math
- 蓝图 MathExpr
- 蓝图 NetReplication
- 蓝图 Random
- 蓝图 Rotation
- 蓝图 Select
- 蓝图 ShapeTrace
- 蓝图 Spline
- 蓝图 Timeline
- 蓝图 Vector
- 蓝图 Camera
- 蓝图 Physics
- 蓝图 Text
- 蓝图 AsyncLoad
- 蓝图 Audio
- 蓝图 CharacterMove
- 蓝图 Component
- 蓝图 EnhancedInput
- 蓝图 Operator
- 蓝图 PredictProjectile
- 蓝图 Reference
- 蓝图 Subsystem
- 蓝图 Transform

### 开发中（1）

- 蓝图 RuntimeComponent

## 材质

### 已完成（27）

- 材质 Material ARCamera
- 材质 Material ArcTrigLog
- 材质 Material Attributes
- 材质 Material CollectionPerInstance
- 材质 Material Compare
- 材质 Material Constant
- 材质 Material ControlFlowCustom
- 材质 Material CustomNormalOutputs
- 材质 Material Decal
- 材质 Material DistanceField
- 材质 Material Function
- 材质 Material If
- 材质 Material Interpolation
- 材质 Material Landscape
- 材质 Material LayersAndChannelMask
- 材质 Material LightVector
- 材质 Material NormalAndDepth
- 材质 Material ParamsExtra
- 材质 Material ScalarParam
- 材质 Material SceneTextures
- 材质 Material SkyAtmosphere
- 材质 Material StaticBool
- 材质 Material Switches
- 材质 Material Transform
- 材质 Material VertexAndInterpolator
- 材质 Material VolumeCloud
- 材质 Material WorldPosition

### 开发中（5）

- 材质 Material ParticleExpressions
- 材质 Material ParticleExtra
- 材质 Material PreSkinnedSources
- 材质 Material SpaceVectorBasics
- 材质 Material TimeAndScene

### 计划中（29）

- 材质 向量参数
- 材质 向量运算
- 材质 噪声
- 材质 图内工具节点
- 材质 坐标变换
- 材质 头发 / Strata / 自定义输出
- 材质 头发（Hair）
- 材质 字体 / 贴图工具
- 材质 数学函数
- 材质 时间 / 场景采样
- 材质 替换 / 开关补充
- 材质 替换 / 渲染开关
- 材质 杂项 2
- 材质 杂项数学 / 工具表达式
- 材质 特殊数学
- 材质 相机 / 视效采样
- 材质 空间向量基础
- 材质 算术补充
- 材质 算术运算
- 材质 粒子补充
- 材质 粒子表达式
- 材质 纹理对象
- 材质 纹理采样
- 材质 自定义 HLSL
- 材质 蒙皮 / 预变形
- 材质 衰减 / 裁剪 / 局部
- 材质 运行期虚拟纹理 RVT
- 材质 通道遮罩
- 材质 速度树

## 其他

### 开发中（1）

- 引擎基础 Engine List

### 计划中（18）

- 硬表面 Array
- 硬表面 Bevel
- 硬表面 Boolean
- 硬表面 Bridge
- 硬表面 Create
- 硬表面 Deform
- 硬表面 Extrude
- 硬表面 Inset
- 硬表面 Loop Cut
- 硬表面 Merge
- 硬表面 Mirror
- 硬表面 Solidify
- 硬表面 Subdivide
- 硬表面 Transform
- 硬表面 关卡摆放 / 批量放置
- 硬表面 基元创建与 StaticMesh 输出
- 硬表面 多操作组合流水线
- 硬表面 流水线声明

> 本路线图仅列出项目展示名，具体进度数据以最新素材收集快照为准。
