using System;
using System.Collections.Generic;
using VGModAPI;

namespace VGSilos.Tests.Support;

/// <summary>
/// Scriptable stand-in for the API's save-data service. Records the single
/// provider it was handed and replays capture/restore/validate against it;
/// registration refusal is configured, not simulated by reflection.
/// </summary>
internal sealed class FakeSaveDataService : ISaveDataService
{
    public List<PersistenceProvider> Providers { get; } = new();
    public SaveDataRegistrationStatus NextStatus { get; set; } = SaveDataRegistrationStatus.Registered;
    public string NextDetail { get; set; } = "test";
    public int RegisterCalls { get; private set; }
    public Exception? ThrowOnRegister { get; set; }
    public Func<ISaveDataRegistration>? RegistrationFactory { get; set; }

    public ServiceAvailability Availability { get; set; } = ServiceAvailability.Available;
    public event Action<ServiceAvailability>? AvailabilityChanged { add { } remove { } }

    public SaveDataRegistrationResult Register(PersistenceProvider provider)
    {
        RegisterCalls++;
        if (ThrowOnRegister != null) throw ThrowOnRegister;
        Providers.Add(provider);
        if (NextStatus == SaveDataRegistrationStatus.Registered)
            return new SaveDataRegistrationResult(NextStatus, RegistrationFactory?.Invoke() ?? new FakeRegistration(), NextDetail);
        return new SaveDataRegistrationResult(NextStatus, null, NextDetail);
    }

    internal sealed class FakeRegistration : ISaveDataRegistration
    {
        public SaveDataState State { get; set; } = new(SaveDataStateKind.Ready, Guid.NewGuid());
        public bool CanRead { get; set; } = true;
        public bool CanMutate { get; set; } = true;
        public event Action<SaveDataState>? StateChanged { add { } remove { } }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
