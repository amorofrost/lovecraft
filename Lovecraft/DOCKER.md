## LoveCraft Backend - Docker Instructions

### Prerequisites

- Docker Desktop installed and running
- .NET 10 SDK (optional, only if you want to run without Docker)

### Quick Start with Docker Compose

1. **Build and run the container**:
   ```bash
   docker-compose up --build
   ```

2. **Access the API**:
   - API: http://localhost:5000
   - Swagger UI: http://localhost:5000/swagger
   - Health Check: http://localhost:5000/health

3. **Stop the container**:
   ```bash
   docker-compose down
   ```

### Alternative: Build and Run with Docker CLI

1. **Build the Docker image**:
   ```bash
   docker build -t lovecraft-backend .
   ```

2. **Run the container**:
   ```bash
   docker run -d -p 5000:8080 --name lovecraft-api lovecraft-backend
   ```

3. **View logs**:
   ```bash
   docker logs lovecraft-api
   ```

4. **Stop and remove**:
   ```bash
   docker stop lovecraft-api
   docker rm lovecraft-api
   ```

### Local Development (without Docker)

1. **Restore dependencies**:
   ```bash
   dotnet restore
   ```

2. **Build the solution**:
   ```bash
   dotnet build
   ```

3. **Run the backend**:
   ```bash
   cd Lovecraft.Backend
   dotnet run
   ```

4. **Access the API**:
   - API: http://localhost:5000 (or check console output for port)
   - Swagger UI: http://localhost:5000/swagger

### Available API Endpoints

Once running, check Swagger UI at http://localhost:5000/swagger for full API documentation.

#### Quick endpoint list:

- **Health**: `GET /health`
- **Users**: 
  - `GET /api/v1/users` - Get list of users
  - `GET /api/v1/users/{id}` - Get user by ID
  - `PUT /api/v1/users/{id}` - Update user
- **Events**:
  - `GET /api/v1/events` - Get all events
  - `GET /api/v1/events/{id}` - Get event by ID
  - `POST /api/v1/events/{id}/register` - Register for event
  - `DELETE /api/v1/events/{id}/register` - Unregister from event
- **Matching**:
  - `POST /api/v1/matching/likes` - Send a like
  - `GET /api/v1/matching/likes/sent` - Get sent likes
  - `GET /api/v1/matching/likes/received` - Get received likes
  - `GET /api/v1/matching/matches` - Get matches
- **Store**:
  - `GET /api/v1/store` - Get store items
  - `GET /api/v1/store/{id}` - Get store item by ID
- **Blog**:
  - `GET /api/v1/blog` - Get blog posts
  - `GET /api/v1/blog/{id}` - Get blog post by ID
- **Forum**:
  - `GET /api/v1/forum/sections` - Get forum sections
  - `GET /api/v1/forum/sections/{sectionId}/topics` - Get topics

### Testing with curl

```bash
# Health check
curl http://localhost:5000/health

# Get users
curl http://localhost:5000/api/v1/users

# Get events
curl http://localhost:5000/api/v1/events

# Get blog posts
curl http://localhost:5000/api/v1/blog
```

### Current Status

**This is a MOCK implementation** - All data is hardcoded and in-memory. No actual database is used yet.

**Current Features**:
- ✅ REST API with stub implementations
- ✅ Swagger/OpenAPI documentation
- ✅ Mock data (matches frontend mock data)
- ✅ CORS enabled for frontend
- ✅ Docker support
- ✅ Health check endpoint

**Not Yet Implemented**:
- ❌ Authentication/Authorization (JWT)
- ❌ Azure Storage integration
- ❌ Data persistence
- ❌ Real-time messaging (SignalR)
- ❌ Input validation
- ❌ Error handling middleware
- ❌ Logging to Application Insights
- ❌ Unit tests

### Next Steps

See the main [README.md](../README.md) and documentation in the `docs/` folder for:
- Backend implementation plan
- Architecture overview
- Azure Storage schema
- Authentication design
- Deployment guide
