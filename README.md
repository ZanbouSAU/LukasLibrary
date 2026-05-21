# CoreLib

**解决方案** · LukasLibrary &nbsp;|&nbsp; **作者** · Lukas Zanbou

---

## 简介

CoreLib 是 LukasLibrary 解决方案的核心基础库，提供了一套面向性能优化的控制台 I/O 实现。
它完全绕过 .NET 运行时标准库中的 `System.Console`，直接面向底层系统调用，
通过两级缓冲（字符缓冲 + 字节缓冲）、零分配格式化和跨平台 PAL 层，
在保持 API 简洁性的同时大幅降低 I/O 开销。

---

## 架构概览

```
CoreLib
├── Console                  # 静态入口，对外暴露全部 I/O API
│   ├── In  ──► TextReader   # 标准输入抽象
│   ├── Out ──► TextWriter   # 标准输出抽象
│   └── Error──► TextWriter  # 标准错误抽象
│
├── TextReader               # 抽象基类：逐字符 / 逐行读取
│   └── StreamReader         # 实现：带字节+字符双缓冲的 stdin 读取器
│
├── TextWriter               # 抽象基类：多类型写入 + 零分配格式化
│   └── StreamWriter         # 实现：带字节+字符双缓冲的 stdout/stderr 写入器
│       ├── StreamWriter.CharBuffer   # 分部：UTF-16 字符缓冲区字段与逻辑
│       └── StreamWriter.ByteBuffer  # 分部：UTF-8 字节缓冲区字段与刷新逻辑
│
└── ConsolePal               # 跨平台 PAL 层：封装 Windows / Unix 系统调用
```

---

## 核心设计

### 两级缓冲流水线

```
Write(string / char[] / IUtf8SpanFormattable / ...)
        │
        ▼
 ┌─────────────┐     满 / Flush      ┌─────────────┐    ByteFlush    ┌──────────────┐
 │ 字符缓冲区   │  ──────────────►   │ 字节缓冲区   │  ──────────────► │ 系统调用输出  │
 │  (UTF-16)   │  ConvertUtf16To    │  (UTF-8)    │  ConsolePal     │ stdout/stderr│
 │  _charBuffer│  ByteBuffer()      │  _byteBuffer│  .Write()       │              │
 └─────────────┘                    └─────────────┘                 └──────────────┘
```

- **字符缓冲区**：暂存 UTF-16 字符，攒够后批量转码，减少编码转换频率。
- **字节缓冲区**：暂存 UTF-8 字节，攒够后批量写出，减少系统调用次数。
- 两级均可独立开关，也可调整容量，适应不同吞吐量需求。

### 零分配泛型格式化

`Write<T>(T value) where T : IUtf8SpanFormattable` 采用三级内存策略，
按序尝试，一旦成功立即写出，不产生任何托管堆分配：

| 优先级 | 缓冲区来源 | 大小 |
|:---:|---|---|
| 1 | `stackalloc` | 256 字节 |
| 2 | `stackalloc` | 2 048 字节 |
| 3 | `ArrayPool<byte>.Shared` | 4 096 字节 |
| 4 | 回退 `ToString()` | — |

### 跨平台 PAL（ConsolePal）

| 平台 | 句柄获取 | 读取 | 写入 |
|---|---|---|---|
| **Windows** | `Kernel32.GetStdHandle` | `Kernel32.ReadFile` | `Kernel32.WriteFile` |
| **Unix / macOS** | 文件描述符（0 / 1 / 2） | `Sys.Read` | `Sys.Write` |

上层代码无需感知平台差异，`ConsolePal.Read / Write` 统一屏蔽两套 API。

### 线程安全

- `Console.In / Out / Error` 属性使用 **双重检查锁定 + `Volatile.Read/Write`** 实现懒加载，
  保证多线程首次访问时只创建一个实例。
- `StreamWriter` 和 `StreamReader` 的所有操作均由私有 `Lock` 字段串行化。
- `SetIn / SetOut / SetError` 替换流时原子地完成赋值与旧流的释放。

---

## 项目结构

```
CoreLib/
├── Console.cs                    # 静态控制台门面类
├── TextWriter.cs                 # 文本写入器抽象基类
├── TextReader.cs                 # 文本读取器抽象基类
├── StreamWriter.cs               # 控制台写入器主实现
├── StreamWriter.CharBuffer.cs    # 字符缓冲区分部（UTF-16 编码转换）
├── StreamWriter.ByteBuffer.cs    # 字节缓冲区分部（系统调用写出）
├── StreamReader.cs               # 控制台读取器主实现
├── StreamReader.CharBuffer.cs    # 字符缓冲区分部
├── StreamReader.ByteBuffer.cs    # 字节缓冲区分部
└── ConsolePal.cs                 # 跨平台系统调用封装
```

---

## 快速上手

CoreLib 的 API 有意设计得与 `System.Console` 高度相似，迁移成本极低。

```csharp
using CoreLib;

// 写入基本类型（零分配，直接格式化为 UTF-8）
Console.WriteLine(42);
Console.WriteLine(3.14f);
Console.WriteLine(true);

// 写入字符串
Console.WriteLine("Hello, World!");

// 写入字符序列（避免字符串分配）
ReadOnlySpan<char> span = stackalloc char[] { 'H', 'i' };
Console.WriteLine(span);

// 写入原始 UTF-8 字节
Console.Write(new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F });

// 从标准输入读取
int ch   = Console.Read();       // 读取单个字符
string? line = Console.ReadLine();  // 读取一行
```

### 缓冲区调优

```csharp
// 调整缓冲区大小（字符缓冲 1024，字节缓冲 8192）
Console.SetBufferSize(charSize: 1024, byteSize: 8192);

// 禁用字符缓冲（字符数据直接进入字节缓冲区转换）
Console.EnableCharBuffer(false);

// 禁用字节缓冲（每次写入直接发起系统调用，适合调试场景）
Console.EnableByteBuffer(false);

// 启用自动刷新（每次 WriteLine 后立即刷新，适合交互式场景）
Console.EnableAutoFlush(true);

// 手动刷新所有缓冲区
Console.FlushBuffer();
```

### 替换标准流

```csharp
// 将标准输出重定向到自定义写入器
Console.SetOut(myCustomWriter);

// 替换后旧的写入器将被自动释放（若实现了 IDisposable）
```

---

## 非托管内存管理

`StreamWriter` 和 `StreamReader` 均通过 `NativeMemory.Alloc` 在非托管堆上分配缓冲区，
以避免 GC 固定（pin）内存带来的停顿开销。释放遵循标准的 **Dispose 模式**：

- **显式释放**（推荐）：通过 `using` 或手动调用 `Dispose()`，写入器会先刷新缓冲区再释放内存。
- **析构函数兜底**：即使未调用 `Dispose()`，终结器也会释放非托管内存，但**不会刷新缓冲区**，可能导致末尾数据丢失。
- 进程退出时，`Console` 静态构造函数注册的 `ProcessExit` 事件会自动刷新 `Out` 和 `Error`，作为最后一道保障。

---

## 依赖

| 依赖 | 用途 |
|---|---|
| `CoreLib.Interop.Windows.Kernel32` | Windows 平台 Win32 API 互操作 |
| `CoreLib.Interop.Unix.System.Native` | Unix 平台 POSIX 系统调用互操作 |
| `System.Text.Unicode.Utf8` | UTF-16 → UTF-8 编码转换 |
| `System.Buffers.ArrayPool<T>` | 泛型格式化写入的数组池复用 |

---

## 许可证

本项目隶属于 **LukasLibrary** 解决方案，版权归 **Lukas Zanbou** 所有。
