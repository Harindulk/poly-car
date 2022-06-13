//Stylized Grass Shader
//Staggart Creations (http://staggart.xyz)
//Copyright protected under Unity Asset Store EULA

//Properties
TEXTURE2D(_WindMap); SAMPLER(sampler_WindMap);
float4 _GlobalWindParams;
//X: Strength
//W: (int bool) Wind zone present
float _WindStrength;
//float4 _WindDirection;

//Nature Renderer parameters
float3 g_WindDirection;
float4 g_SmoothTime;
float4 g_Wind;

struct WindSettings
{
	float mask;
	float ambientStrength;
	float speed;
	float3 direction;
	float swinging;

	float randObject;
	float randVertex;
	float randObjectStrength;

	float gustStrength;
	float gustFrequency;
};

WindSettings PopulateWindSettings(in float strength, float speed, float4 direction, float swinging, float mask, float randObject, float randVertex, float randObjectStrength, float gustStrength, float gustFrequency)
{
	WindSettings s = (WindSettings)0;

	//Apply WindZone strength
	if (_GlobalWindParams.w > 0) 
	{
		strength *= _GlobalWindParams.x;
		gustStrength *= _GlobalWindParams.x;
		//direction.xz += _WindDirection.xz;
	}

	s.ambientStrength = strength;
	s.speed = speed;
	s.direction.xz = normalize(float2(direction.x, direction.z));
	s.direction.y = 0;
	s.swinging = swinging;
	s.mask = mask;
	s.randObject = randObject;
	s.randVertex = randVertex;
	s.randObjectStrength = randObjectStrength;
	s.gustStrength = gustStrength;
	s.gustFrequency = gustFrequency;

	//Nature renderer params
	if (g_SmoothTime.x > 0) {
		s.ambientStrength *= g_Wind.y;
		s.gustStrength *= g_Wind.y;
		s.direction.xz = g_WindDirection.xz;
	}


	return s;
}

//World-align UV moving in wind direction
float2 GetGustingUV(float3 wPos, in float speed, in float freq, in float3 dir)
{
	return (wPos.xz * freq * 0.01) - (_TimeParameters.x * speed * freq * 0.01) * dir.xz;
}

//World-align UV moving in wind direction
float2 GetGustingUV(float3 wPos, WindSettings s)
{
	return GetGustingUV(wPos, s.speed, s.gustFrequency, s.direction);
}

float SampleGustMapLOD(float3 wPos, WindSettings s) 
{
	float2 gustUV = GetGustingUV(wPos, s);
	float gust = SAMPLE_TEXTURE2D_LOD(_WindMap, sampler_WindMap, gustUV, 0).r;

	gust *= s.gustStrength * s.mask;

	return gust;
}

float SampleGustMap(float3 wPos, WindSettings s)
{
	float2 gustUV = GetGustingUV(wPos, s);

	float gust = SAMPLE_TEXTURE2D(_WindMap, sampler_WindMap, gustUV).r;

	gust *= s.gustStrength * s.mask;

	return gust;
}

float4 GetWindOffset(in float3 positionOS, in float3 wPos, float rand, WindSettings s)
{
	float4 offset;

	//Random offset per vertex
	float f = length(positionOS.xz) * s.randVertex;
	float strength = s.ambientStrength * 0.5 * lerp(1, rand, s.randObjectStrength);
	
	//Combine
	float2 sine = sin(s.speed * (_TimeParameters.x + (rand * s.randObject) + f));
	//Remap from -1/1 to 0/1
	sine = lerp(sine * 0.5 + 0.5, sine, s.swinging);

	//Apply gusting
	float2 gust = SampleGustMapLOD(wPos, s).xx;

	//Scale sine
	sine = sine * s.mask * strength;

	//Mask by direction vector + gusting push
	offset.xz = (sine + gust) * s.direction.xz;
	offset.y = s.mask;

	//Summed offset strength
	float windWeight = length(offset.xz) + 0.0001;
	//Slightly negate the triangle-shape curve
	windWeight = pow(windWeight, 1.5);
	offset.y *= windWeight;

	//Wind strength in alpha
	offset.a = windWeight;

	return offset;
}