/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Детерминированная машина состояний жизненного цикла камеры (P0: гонки и краши превью)
 * СОДЕРЖИМОЕ: CameraLifecycleController, CameraLifecycleState
 * ЗАВИСИМОСТИ: System
 * ==============================================================================
 */

namespace GosTek.Services;

public enum CameraLifecycleState
{
    Stopped,
    Starting,
    Running,
    Disposed
}

/// <summary>
/// Потокобезопасная машина состояний камеры для защиты от гонок при быстром переключении вкладок.
/// </summary>
public sealed class CameraLifecycleController
{
    private readonly object _lock = new();
    private CameraLifecycleState _state = CameraLifecycleState.Stopped;
    private int _activeToken = -1;

    public CameraLifecycleState State
    {
        get
        {
            lock (_lock)
                return _state;
        }
    }

    public bool IsRunning => State == CameraLifecycleState.Running;
    public bool IsStarting => State == CameraLifecycleState.Starting;
    public bool IsStopped => State == CameraLifecycleState.Stopped;
    public bool IsDisposed => State == CameraLifecycleState.Disposed;

    /// <summary>
    /// Попытка перевести камеру в состояние Starting.
    /// Возвращает true только если камера была в состоянии Stopped.
    /// </summary>
    public bool TryBeginStart(int token)
    {
        lock (_lock)
        {
            if (_state != CameraLifecycleState.Stopped)
                return false;

            _state = CameraLifecycleState.Starting;
            _activeToken = token;
            return true;
        }
    }

    /// <summary>
    /// Завершение запуска камеры.
    /// Возвращает true только если камера все еще Starting и токен совпадает.
    /// Если камера была отменена/остановлена во время запуска, возвращает false.
    /// </summary>
    public bool CompleteStart(int token)
    {
        lock (_lock)
        {
            if (_state == CameraLifecycleState.Starting && _activeToken == token)
            {
                _state = CameraLifecycleState.Running;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Остановка камеры и сброс активного токена.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_state == CameraLifecycleState.Disposed)
                return;

            _state = CameraLifecycleState.Stopped;
            _activeToken = -1;
        }
    }

    /// <summary>
    /// Окончательное освобождение ресурсов.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _state = CameraLifecycleState.Disposed;
            _activeToken = -1;
        }
    }
}
