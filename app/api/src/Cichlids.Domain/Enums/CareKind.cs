namespace Cichlids.Domain.Enums;

public enum CareKind { WaterChange, Measurement }

public enum CareAmountUnit { Liter, UsGallon, Percent }

public enum CareParameter { Temperature, Ph, Kh, Gh, No2, No3, Nh4, Po4, Conductivity, Tds }

public enum MeasurementUnit { None, Celsius, Fahrenheit, Dgh, Dkh, Ppm, MgPerLiter, MicrosiemensPerCm }

public enum MeasurementOrigin { TestKit, Meter, Probe }
