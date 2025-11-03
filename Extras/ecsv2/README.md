# ecsv2 - Roblox Analytics Service

ASP.NET Core-based analytics tracking service that mimics the original Roblox ecsv2 subdomain.

## Overview

This service provides analytics tracking endpoints that accept form interactions, user events, and other analytics data, returning a 1x1 transparent tracking pixel.

## Endpoints

### `/www/e.png` - Main Analytics Tracking

Primary endpoint for tracking user interactions and form events.

**Query Parameters:**
- `aType` - Action type (e.g., "focus", "blur")
- `field` - Form field name (e.g., "username", "passwordConfirm")
- `evt` - Event type (e.g., "formInteraction")
- `ctx` - Context/Form name (e.g., "RollerCoasterSignupForm")
- `url` - Source page URL (URL encoded)
- `lt` - Local timestamp in ISO format

**Example:**
```
http://localhost:5078/www/e.png?aType=focus&field=passwordConfirm&evt=formInteraction&ctx=RollerCoasterSignupForm&url=http%3A%2F%2Flocalhost%3A5077%2F&lt=2016-11-07T14%3A51%3A26.750Z
```

**Response:** Returns a 1x1 transparent PNG image

### `/pe` - Platform Events

Endpoint for studio and diagnostic events.

**Query Parameters:**
- `t` - Event type (e.g., "studio", "diagnostic")

**Example:**
```
http://localhost:5078/pe?t=studio
```

### `/api/analytics` - View Collected Data

Debug endpoint to view collected analytics events.

**Query Parameters:**
- `limit` - Number of recent events to return (default: 100)

**Example:**
```
http://localhost:5078/api/analytics?limit=50
```

**Response:** JSON array of analytics events

### `/` - Health Check

Returns service information and available endpoints.

## Running the Service

### Using .NET CLI
```bash
cd ecsv2
dotnet run
```

### Using Visual Studio
Open `ecsv2.sln` and press F5 to run.

The service will start on `http://localhost:5078`

## Features

- ✅ CORS enabled for cross-origin requests
- ✅ In-memory storage for analytics events (max 10,000 events)
- ✅ Structured logging to console
- ✅ Returns proper 1x1 transparent PNG tracking pixel
- ✅ Captures IP address and User-Agent
- ✅ No-cache headers for accurate tracking

## Integration with Roblox Webserver

The main Roblox webserver can make requests to this service using JavaScript:

```javascript
// Example: Track form interaction
var img = new Image();
img.src = "http://localhost:5078/www/e.png?" + 
    "aType=focus&" +
    "field=username&" +
    "evt=formInteraction&" +
    "ctx=SignupForm&" +
    "url=" + encodeURIComponent(window.location.href) + "&" +
    "lt=" + new Date().toISOString();
```

## Original Roblox EventStream

The original Roblox implementation used:
```javascript
Roblox.EventStream.Init(
    "//ecsv2.roblox.com/www/e.png",
    "//ecsv2.roblox.com/www/e.png",
    "//ecsv2.roblox.com/pe?t=studio",
    "//ecsv2.roblox.com/pe?t=diagnostic"
);
```

## Notes

- Analytics events are stored in memory and will be lost on restart
- For production use, implement persistent storage (database)
- Consider adding authentication/rate limiting for production deployments
- The tracking pixel is a valid 1x1 transparent PNG image
