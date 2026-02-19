# API Testing Guide

## Quick Test Commands

Once the backend is running (see [DOCKER.md](./DOCKER.md)), you can test the API endpoints using these commands:

### Health Check

```bash
curl http://localhost:5000/health
```

Expected response:
```json
{
  "status": "Healthy",
  "timestamp": "2024-12-01T10:00:00Z",
  "version": "1.0.0"
}
```

### Users API

**Get all users:**
```bash
curl http://localhost:5000/api/v1/users
```

**Get specific user:**
```bash
curl http://localhost:5000/api/v1/users/1
```

**Update user:**
```bash
curl -X PUT http://localhost:5000/api/v1/users/1 \
  -H "Content-Type: application/json" \
  -d '{
    "id": "1",
    "name": "Anna Updated",
    "age": 26,
    "bio": "Updated bio",
    "location": "Moscow",
    "gender": 1
  }'
```

### Events API

**Get all events:**
```bash
curl http://localhost:5000/api/v1/events
```

**Get specific event:**
```bash
curl http://localhost:5000/api/v1/events/1
```

**Register for event:**
```bash
curl -X POST http://localhost:5000/api/v1/events/1/register
```

**Unregister from event:**
```bash
curl -X DELETE http://localhost:5000/api/v1/events/1/register
```

### Matching API

**Send a like:**
```bash
curl -X POST http://localhost:5000/api/v1/matching/likes \
  -H "Content-Type: application/json" \
  -d '{
    "toUserId": "2"
  }'
```

**Get sent likes:**
```bash
curl http://localhost:5000/api/v1/matching/likes/sent
```

**Get received likes:**
```bash
curl http://localhost:5000/api/v1/matching/likes/received
```

**Get matches:**
```bash
curl http://localhost:5000/api/v1/matching/matches
```

### Store API

**Get all store items:**
```bash
curl http://localhost:5000/api/v1/store
```

**Get specific item:**
```bash
curl http://localhost:5000/api/v1/store/s1
```

### Blog API

**Get all blog posts:**
```bash
curl http://localhost:5000/api/v1/blog
```

**Get specific post:**
```bash
curl http://localhost:5000/api/v1/blog/b1
```

### Forum API

**Get forum sections:**
```bash
curl http://localhost:5000/api/v1/forum/sections
```

**Get topics in a section:**
```bash
curl http://localhost:5000/api/v1/forum/sections/general/topics
```

## Using Swagger UI

The easiest way to test the API is through Swagger UI:

1. Start the backend (see [DOCKER.md](./DOCKER.md))
2. Open http://localhost:5000/swagger in your browser
3. Explore and test all endpoints interactively

## Using Postman

1. Import the OpenAPI spec from http://localhost:5000/swagger/v1/swagger.json
2. Create a new collection in Postman
3. Set base URL to `http://localhost:5000`
4. Test endpoints

## Response Format

All API responses follow this format:

**Success Response:**
```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "timestamp": "2024-12-01T10:00:00Z"
}
```

**Error Response:**
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "ERROR_CODE",
    "message": "Error description",
    "details": {}
  },
  "timestamp": "2024-12-01T10:00:00Z"
}
```

## Integration Testing

You can write integration tests in your frontend like this:

```javascript
// Example: Fetch users
const response = await fetch('http://localhost:5000/api/v1/users');
const result = await response.json();
if (result.success) {
  console.log('Users:', result.data);
} else {
  console.error('Error:', result.error);
}
```

## Mock Data

The API currently returns mock data defined in `MockDataStore.cs`. This matches the mock data from the frontend React application.

Available mock data:
- 4 users with profiles
- 4 events (concerts, meetups, festivals)
- 4 store items
- 3 blog posts
- 4 forum sections
- 3 songs

## Next Steps

- Replace mock services with real Azure Storage implementation
- Add JWT authentication
- Add input validation
- Add error handling middleware
- Add logging
- Add rate limiting
