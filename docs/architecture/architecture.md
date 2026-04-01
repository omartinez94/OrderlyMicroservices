# AGENTS.md - Restaurant Management System

## Project Overview

This is a **multi-tenant restaurant management system** designed to streamline operations for restaurants under the same brand. The system handles order management, kitchen operations, reservations, menu management, and customer engagement with real-time synchronization between front-of-house and kitchen.

**Target Market:** Multiple restaurant locations under the same brand  
**Initial Scale:** 10-20 concurrent users per restaurant, 15 orders/hour peak  
**Tech Stack:** .NET 8 + React 18 + PostgreSQL + Redis + RabbitMQ  
**Architecture:** Microservices with event-driven communication

---

## 🎯 Core Business Requirements

### Order Management
- **Order Types:** Dine-in, Takeout, Delivery
- **Order Workflow:** Ordering → Pending → Confirmed → Preparing → Ready → Delivered → Completed
- **Key Features:**
  - Real-time kitchen synchronization (< 1 second latency)
  - Order modifications at different stages with approval workflows
  - Bill splitting (Equal & Custom - NO splitting by individual items)
  - Manager approval required for modifications after "Ready" status
  - Modified "Ready" orders revert to "Preparing" with flag
  - Excel bulk upload for offline orders (manager uploads, admin approves)
  
### Menu System
- **Structure:** Categories → SubCategories → Menu Items
- **Features:**
  - Mandatory item photos
  - Variations (Size, Spice Level, etc.) with price modifiers
  - Combos (bundled items)
  - Seasonal items (date-range availability)
  - Promotional pricing (time-based)
  - Deep customizations (ingredient add/remove/substitute)
  - Ingredient tracking with availability management
  - **Auto-substitute:** Admins can configure automatic ingredient substitutions
  
### Kitchen Operations
- **Display Requirements:**
  - High visibility and clarity (large screen optimized)
  - Color-coded time status: 🟢 Green (< 80% prep time), 🟡 Yellow (80-100%), 🔴 Red (overdue)
  - Sound alerts for new orders, approaching deadlines, overdue orders
  - Auto-refresh every 5 seconds via SignalR
  - All stations see same information (multi-station support deferred)
  - Kitchen staff can modify orders at any time except when "Ready"

### Reservations & Queue
- **Reservations:**
  - Manual or automatic confirmation (configurable per restaurant)
  - Table assignment with capacity matching
  - Reminder notifications (1 hour before)
  - No-show tracking
  - No deposit required (configurable future option)
  
- **Walk-in Queue:**
  - Queue management when no tables available
  - Estimated wait time calculation: `(OccupiedTables / TotalTables) × EstimatedTurnoverMinutes`
  - SMS/WhatsApp notification when table ready
  - 10-minute response window after notification

### Table Management
- **Floor Plan:** Visual layout with drag-drop positioning
- **Table Properties:** Number, capacity, position (X, Y), shape (rectangle/circle/square)
- **Table Status:** Available, Occupied, Reserved, Cleaning, Needs Attention
- **Table Merging:** Support for combining tables for large parties
- **Table Transfer:** Move orders between tables

### Customer Engagement
- **Feedback System:**
  - Post-order survey (food quality, service speed, waiter friendliness, overall - 1-5 stars)
  - Automatic reward generation based on ratings (4+ stars = 10% off, 5 stars = 15% + free appetizer)
  - Reward codes valid 30 days, one-time use
  - Survey accessible via QR code on receipt or WhatsApp/Email link
  
- **Notifications:**
  - Order receipts (WhatsApp/Email)
  - Reservation confirmations and reminders
  - Delivery status updates
  - No SMS for now (future consideration)

### Analytics & Reporting
- **Required Reports:**
  - Daily/monthly sales by restaurant
  - Most ordered items (overall and by time of day)
  - Order timing analytics (track every status transition)
  - Menu item performance (times ordered, revenue, prep time accuracy)
  - Waiter performance (orders taken, avg order value, customer satisfaction)
  
- **Real-time Dashboards:**
  - Kitchen order overview (clarity and visibility priority)
  - Waiter order overview (easy modification access)
  - Quick order search for historical orders

---

## 🏗️ Microservices Architecture

### 1. Identity Service (Port 5007)
**Technology:** Custom implementation using ASP.NET Core Identity + JWT

**Responsibilities:**
- User authentication and authorization
- Role-based access control (RBAC)
- Multi-restaurant user assignment
- JWT token generation and validation (15-min access, 7-day refresh)
- Session management
- Audit logging

**Database (PostgreSQL):**
```
- Users (extends IdentityUser)
- Roles (SuperAdmin, RestaurantAdmin, Manager, KitchenManager, Waiter, KitchenStaff, Host, Cashier)
- Permissions (granular: orders:create, menu:edit, etc.)
- UserRoles (junction, supports multiple roles per user)
- RolePermissions (junction)
- UserRestaurants (multi-restaurant access)
- RefreshTokens
- LoginAuditLog
```

**User Roles:**
- **SuperAdmin:** System-wide control, manage all restaurants
- **RestaurantAdmin:** Full control within assigned restaurant(s)
- **Manager:** Operational management, approve modifications, view reports
- **KitchenManager:** Kitchen oversight, manage kitchen staff
- **Waiter/Server:** Create/modify orders (limited by status)
- **KitchenStaff:** View orders, update prep status
- **Host/Hostess:** Manage reservations, assign tables, walk-in queue
- **Cashier:** Process payments, split bills

**Authentication Flow:**
1. User logs in → Identity Service validates credentials
2. Generate JWT with userId, roles, restaurantId, permissions
3. Client includes JWT in Authorization header for all requests
4. API Gateway validates token (stateless JWT validation)
5. Services check permissions from JWT claims

**Key Security Features:**
- Bcrypt password hashing (cost factor 12)
- Account lockout (5 failed attempts = 30-min lock)
- Rate limiting (5 login attempts per 15 minutes per IP)
- Audit trail for all authentication events
- HTTPS-only communication

---

### 2. Catalog Service (Port 5001)
**Responsibilities:**
- Restaurant management (CRUD)
- Menu management (categories, items, variations, combos)
- Ingredient tracking and availability
- Table and floor plan management
- Menu item analytics aggregation

**Database (PostgreSQL):**
```
- Restaurants (multi-tenant, stores tax rate, currency, timezone, configuration flags)
- Tables (table number, capacity, position X/Y, shape, status, current order)
- MergedTables (parent-child table relationships)
- MenuCategories, MenuSubCategories
- MenuItems (base price, mandatory image URL, prep time range, item type, availability status)
- MenuItemVariations (size, spice level, price modifiers)
- ComboItems (combo definitions with included items)
- Ingredients (name, unit, current stock, minimum stock, availability)
- MenuItemIngredients (junction, quantity required, is optional)
- IngredientAlternatives (original → alternative mapping, price modifier, auto-substitute flag)
- MenuItemAnalytics (daily aggregated stats)
```

**Key Features:**
- **Ingredient Availability Engine:**
  - When ingredient marked unavailable → check alternatives
  - If no alternative exists and ingredient not optional → MenuItem status = "unavailable"
  - If alternative exists → MenuItem status = "limited" (customer chooses substitute)
  - Auto-substitute: If enabled by admin, automatically replace without asking customer
  
- **Menu Caching Strategy:**
  - Full menu cached in Redis (1-hour TTL)
  - Ingredient availability cached (5-min TTL)
  - Invalidate cache on menu/ingredient changes

**Events Published:**
- `MenuItemCreated`, `MenuItemUpdated`, `MenuItemDeleted`
- `IngredientAvailabilityChanged`
- `TableStatusChanged`
- `RestaurantConfigurationChanged`

**Events Consumed:**
- `OrderCompleted` (update MenuItemAnalytics)

---

### 3. Discount Service (Port 5002)
**Responsibilities:**
- Discount rule management (percentage, fixed amount, BOGO)
- Promo code generation and validation
- Reward code management (from customer feedback)
- Seasonal/time-based pricing
- Customer loyalty points (future)

**Database (PostgreSQL):**
```
- Discounts (type, value, rules, active dates)
- PromoCodes (code, discount ID, usage limit, expiry)
- RewardCodes (code, type, value, description, redeemed flag)
- DiscountRules (conditions: minimum order, specific items, time-based)
```

**API Capabilities:**
- Validate promo code before checkout
- Calculate discount for given order items
- Generate reward codes from feedback ratings
- Check reward redemption status

**Events Published:**
- `DiscountApplied`
- `RewardGenerated`
- `RewardRedeemed`

**Events Consumed:**
- `FeedbackSubmitted` (generate reward if rating ≥ 4)
- `OrderCreated` (check for auto-apply discounts)

---

### 4. Basket Service (Port 5003)
**Technology:** Redis-only (no PostgreSQL)

**Responsibilities:**
- Temporary order storage before checkout
- Real-time price calculation (calls Catalog for prices)
- Apply discounts (calls Discount Service)
- Validate item availability
- Support anonymous users (session-based)

**Redis Data Structure:**
```json
basket:{userId}:{restaurantId} → {
  "userId": "uuid",
  "restaurantId": 1,
  "items": [
    {
      "menuItemId": 10,
      "quantity": 2,
      "unitPrice": 12.00,
      "variations": [{"name": "Size", "value": "Large", "price": 2.00}],
      "customizations": [{"ingredient": "Lettuce", "action": "remove"}]
    }
  ],
  "appliedDiscounts": [],
  "subtotal": 24.00,
  "createdAt": "2024-03-31T10:30:00Z",
  "expiresAt": "2024-03-31T12:30:00Z" // 2-hour TTL
}
```

**Why Redis-Only:**
- Fast read/write performance
- Built-in TTL (auto-expire abandoned baskets after 2 hours)
- No need for persistent storage
- Handles high traffic efficiently

**Events Published:**
- `BasketCreated`
- `BasketAbandoned` (after TTL expires without checkout)

**Events Consumed:**
- `MenuItemPriceChanged` (update basket prices)
- `IngredientAvailabilityChanged` (validate basket items)

---

### 5. Order Service (Port 5004)
**Responsibilities:**
- Order lifecycle management
- Order status state machine enforcement
- Bill splitting (equal/custom, NO item-level splitting)
- Order modifications with approval workflow
- Table assignment
- Delivery tracking (address + status, no real-time map)
- Reservations management
- Walk-in queue management
- Excel bulk order upload

**Database (PostgreSQL):**
```
- Orders (order number, type, status, pricing, timestamps for each status transition)
- OrderItems (menu item, quantity, price, variations JSONB, customizations JSONB, seat number, prep status)
- OrderModificationLog (audit trail: who, what, when, requires approval)
- OrderBills (bill splitting: equal or custom amounts)
- OrderTimingAnalytics (tracks seconds between each status transition)
- Reservations (reservation number, party size, date/time, table, status, approval workflow)
- WalkInQueue (customer name, party size, estimated wait, status)
- BulkOrderUploads (file name, rows processed, errors, approval status)
```

**Order Status State Machine:**
```
Ordering → Pending → Confirmed → Preparing → Ready → Delivered → Completed
   ↓         ↓          ↓           ↓
Cancelled Cancelled  OnHold    Preparing (with manager flag)
```

**Modification Rules:**
| Status | Who Can Modify | Price Behavior | Special Rules |
|--------|---------------|----------------|---------------|
| Ordering, Pending | Waiter, Manager, Admin | Recalculates | Full edits allowed |
| Confirmed, Preparing | Manager, Admin | Recalculates | Kitchen notified |
| Ready | Manager, Admin | N/A | Requires admin approval, reverts to "Preparing" with flag |
| Delivered, Completed | N/A | N/A | No modifications allowed |

**Bill Splitting Strategies:**
- **Equal Split:** Divide total by N people equally
- **Custom Split:** Manager enters amount per person (must sum to order total)
- **Seat Assignment:** Assign items to seat numbers for easier splitting
- **Validation:** Sum of all bills must equal order total

**Excel Bulk Upload Flow:**
1. Manager uploads Excel file
2. System validates (menu items exist, tables available)
3. Preview shown with errors highlighted (red rows)
4. Manager reviews and confirms
5. Admin approves
6. Bulk insert orders in transaction
7. Notifications sent if configured

**Events Published:**
- `OrderCreated`, `OrderStatusChanged`, `OrderModified`, `OrderCompleted`, `OrderCancelled`
- `BillSplit`
- `ReservationCreated`, `ReservationConfirmed`

**Events Consumed:**
- `BasketCheckedOut` (create order from basket)
- `DiscountApplied` (update order total)
- `KitchenOrderReady` (update status to Ready)

---

### 6. Kitchen Service (Port 5005)
**Technology:** PostgreSQL Read Replica (from Order Service) + Own DB

**Responsibilities:**
- Kitchen dashboard data aggregation
- Order preparation tracking
- Prep status management (pending → preparing → ready)
- Time tracking and alerts
- Kitchen-side order modifications

**Database:**
- **Read Replica:** Orders, OrderItems (read-only)
- **Own DB:** KitchenStations (future), PrepTimeTracking

**Kitchen Dashboard Logic:**
```csharp
Color Status Calculation:
- Green: OrderAge < EstimatedPrepTime × 0.8
- Yellow: OrderAge < EstimatedPrepTime
- Red: OrderAge ≥ EstimatedPrepTime

Sound Alerts:
- Ding: New order arrives
- Chime: Order approaching time limit (yellow → red transition)
- Alarm: Order overdue
```

**Dashboard Auto-Refresh:**
- SignalR push every 5 seconds
- Client-side re-render on data change
- Optimistic UI updates for prep status changes

**Events Published:**
- `KitchenOrderReceived`
- `KitchenOrderStarted`
- `KitchenOrderReady`
- `KitchenOrderModified`

**Events Consumed:**
- `OrderCreated` (add to kitchen queue)
- `OrderStatusChanged` (update display)
- `OrderModified` (update items)

---

### 7. Notification Service (Port 5006)
**Responsibilities:**
- WhatsApp notifications (Twilio)
- Email notifications (SendGrid)
- Receipt generation and sending
- Feedback request automation
- Reservation reminders
- Template management

**Database (PostgreSQL):**
```
- NotificationLog (recipient, channel, message type, status, sent timestamp)
- NotificationTemplates (customizable message templates)
- CustomerFeedback (ratings, comments, reward codes)
- EmailQueue, WhatsAppQueue (processing queues)
```

**Notification Types:**
- **order_confirmation:** Receipt sent after order completion
- **feedback_request:** Survey link sent 30 minutes after delivery
- **reservation_reminder:** 1 hour before reservation time
- **call_waiter:** Kitchen notifies waiter order is ready (future)

**Feedback → Reward Flow:**
1. Order completed → wait 30 minutes
2. Send feedback request via WhatsApp/Email
3. Customer submits survey
4. If OverallRating ≥ 4 → generate reward code
5. Publish `FeedbackSubmitted` event → Discount Service generates reward
6. Include reward code in confirmation message

**Events Published:**
- `NotificationSent`, `NotificationFailed`
- `FeedbackSubmitted`

**Events Consumed:**
- `OrderCompleted` (send receipt + schedule feedback request)
- `ReservationCreated` (send confirmation)
- `ReservationConfirmed` (schedule reminder)

---

## 🔄 Inter-Service Communication

### Synchronous (HTTP/gRPC)
**Use when immediate response required:**
- Basket → Catalog: Get menu item prices
- Basket → Discount: Calculate discount
- Order → Catalog: Validate table availability
- Kitchen → Order: Get order details (via read replica)

### Asynchronous (RabbitMQ Events)
**Use for eventual consistency:**
- Order → Kitchen: `OrderCreated`
- Order → Notification: `OrderCompleted`
- Catalog → Basket: `IngredientAvailabilityChanged`
- Notification → Discount: `FeedbackSubmitted`

**Event Structure:**
```json
{
  "eventId": "uuid",
  "eventType": "OrderCreated",
  "timestamp": "2024-03-31T10:30:00Z",
  "restaurantId": 1,
  "data": {
    "orderId": 123,
    "orderNumber": "R1-20240331-001",
    "orderType": "dine-in",
    "tableId": 5,
    "totalAmount": 45.50
  }
}
```

---

## 🗄️ Database Strategy

### Catalog Service
- Full PostgreSQL database
- Heavy read, light write
- **Cache:** Full menu in Redis (1-hour TTL), Ingredient availability (5-min TTL)

### Discount Service
- PostgreSQL database
- Medium read/write
- **Cache:** Active discounts in Redis

### Basket Service
- **Redis ONLY** (no PostgreSQL)
- High read/write
- TTL-based expiration (2 hours)

### Order Service
- Full PostgreSQL database
- Heavy write, medium read
- **Cache:** Active orders in Redis (manual invalidation)
- **Archive:** Completed orders after 6 months

### Kitchen Service
- PostgreSQL Read Replica (from Order Service)
- Own PostgreSQL for kitchen-specific data
- **Cache:** Dashboard data in Redis (30-second TTL)

### Notification Service
- PostgreSQL database
- Queue-based processing (Hangfire background jobs)
- **Archive:** Old notification logs after 3 months

### Identity Service
- PostgreSQL database
- Medium read, light write
- **Cache:** User permissions in Redis (1-hour TTL)

---

## 📊 Key Database Relationships

### Multi-Tenant Structure
```
Restaurants (1) ──< (M) Users
Restaurants (1) ──< (M) Tables
Restaurants (1) ──< (M) MenuItems
Restaurants (1) ──< (M) Orders

Users (M) >──< (M) Restaurants (via UserRestaurants - multi-restaurant access)
```

### Menu Hierarchy
```
MenuCategories (1) ──< (M) MenuSubCategories
MenuSubCategories (1) ──< (M) MenuItems
MenuItems (1) ──< (M) MenuItemVariations
MenuItems (M) >──< (M) Ingredients (via MenuItemIngredients)
Ingredients (1) ──< (M) IngredientAlternatives
```

### Order Structure
```
Orders (1) ──< (M) OrderItems
Orders (1) ──< (M) OrderBills (bill splitting)
Orders (1) ──< (M) OrderModificationLog (audit trail)
Orders (1) ──< (1) CustomerFeedback
OrderItems (M) ──> (1) MenuItems (reference)
```

### Reservation & Queue
```
Restaurants (1) ──< (M) Reservations
Restaurants (1) ──< (M) WalkInQueue
Reservations (M) ──> (1) Tables
WalkInQueue (M) ──> (1) Tables (when seated)
```

---

## 🔐 Security & Authorization

### JWT Token Structure
```json
{
  "sub": "user-uuid-123",
  "email": "waiter@restaurant.com",
  "name": "John Doe",
  "roles": ["Waiter"],
  "restaurantId": 1,
  "permissions": [
    "orders:create",
    "orders:view_own",
    "orders:modify_ordering"
  ],
  "iss": "IdentityService",
  "aud": "RestaurantManagementSystem",
  "exp": 1234567890
}
```

### Permission Naming Convention
Format: `{resource}:{action}_{scope}`

**Examples:**
- `orders:create`
- `orders:view_own` (only orders created by this user)
- `orders:view_all` (all orders in restaurant)
- `orders:modify_ordering` (can modify orders in "Ordering" status)
- `orders:modify_confirmed` (managers only)
- `orders:modify_ready` (requires admin approval)
- `menu:edit`
- `kitchen:update_prep_status`
- `users:assign_roles`

### Row-Level Security
All services enforce:
- Users can only access data from their assigned restaurant(s)
- Except SuperAdmin who can access all restaurants
- Implemented via `WHERE RestaurantId = @CurrentUserRestaurantId` in queries

---

## 🚀 Technology Stack Details

### Backend
- **.NET 8** (LTS version)
- **ASP.NET Core Web API**
- **Entity Framework Core** (Code-First migrations)
- **SignalR** (real-time communication)
- **Ocelot** (API Gateway)
- **RabbitMQ** (event bus)
- **Hangfire** (background jobs)
- **FluentValidation** (input validation)
- **AutoMapper** (DTO mapping)
- **Serilog** (logging)

### Frontend
- **React 18** with TypeScript
- **Redux Toolkit + RTK Query** (state + caching)
- **React Router v6** (routing)
- **SignalR Client** (@microsoft/signalr)
- **TailwindCSS + HeadlessUI** (UI framework)
- **React Hook Form** (forms)
- **Recharts** (analytics charts)
- **React DnD** (drag-drop for bill splitting, floor plan)

### Database & Caching
- **PostgreSQL 14+** (primary database)
- **Redis 6+** (caching + SignalR backplane)

### Notifications
- **Twilio** (WhatsApp Business API)
- **SendGrid** (Email)

### Infrastructure (Initial)
- **Azure App Service** (Web API + SignalR support)
- **Azure Cache for Redis**
- **Azure Database for PostgreSQL**
- **Azure Blob Storage** (menu images, receipts)

---

## 📏 Performance Targets

### Response Times
- Order creation: < 200ms
- Kitchen dashboard refresh: < 100ms
- Menu load: < 150ms (with caching)
- Search orders: < 300ms
- Daily report generation: < 2 seconds

### Concurrent Users
- 50 users per restaurant (peak)
- 500 total concurrent connections (10 restaurants)
- 15 orders/hour per restaurant (peak)

### Database Indexes (Critical Queries)
```sql
CREATE INDEX idx_orders_restaurant_status ON Orders(RestaurantId, Status);
CREATE INDEX idx_orders_created_desc ON Orders(RestaurantId, CreatedAt DESC);
CREATE INDEX idx_menuitems_available ON MenuItems(RestaurantId, IsAvailable);
CREATE INDEX idx_ingredients_available ON Ingredients(RestaurantId, IsAvailable);
CREATE INDEX idx_reservations_date ON Reservations(RestaurantId, ReservationDate, ReservationTime);
```

### Caching Strategy (Redis)
```
Pattern: {service}:{entity}:{id}

catalog:menu:{restaurantId} → Full menu (TTL: 1 hour)
catalog:ingredients:{restaurantId} → Ingredient availability (TTL: 5 min)
order:active_orders:{restaurantId} → Active orders (manual invalidation)
kitchen:dashboard:{restaurantId} → Kitchen display data (TTL: 30 sec)
identity:user:{userId}:permissions → User permissions (TTL: 1 hour)
basket:{userId}:{restaurantId} → Shopping basket (TTL: 2 hours)
```

---

## 🧪 Testing Strategy

### Unit Tests
- Service layer logic
- State machine transitions
- Permission validation
- Price calculations

### Integration Tests
- API endpoints
- Database operations
- Event publishing/consuming
- Service-to-service calls

### End-to-End Tests
- Complete order flow (create → kitchen → complete)
- Reservation flow
- Bill splitting
- Modification approval workflow

### Performance Tests
- Load testing: 500 concurrent users
- Kitchen dashboard under load (100 active orders)
- Real-time SignalR connection stability

---

## 🎯 Future AI Features (Planned)

### Phase 1 (High Priority)
1. **Smart Order Suggestions:** Predict items customers likely to add based on current order
2. **Inventory Forecasting:** Predict ingredient stockouts based on historical usage
3. **Dynamic Prep Time Prediction:** AI adjusts prep times based on kitchen load

### Phase 2 (Medium Priority)
4. **Sentiment Analysis:** Analyze feedback comments for urgent issues
5. **Demand Forecasting:** Predict busy periods for staff scheduling
6. **Menu Optimization:** Recommend menu changes based on profitability and popularity

### Phase 3 (Future)
7. **Voice-to-Order:** Speech recognition for order entry
8. **Computer Vision:** Verify plate presentation and portion sizes
9. **Chatbot:** WhatsApp bot for reservations and takeout orders

**AI Service (Port 5008)** - To be added when AI features implemented
- ML model training & inference
- TensorFlow/PyTorch or ML.NET
- Feature store in Redis
- Training data pipeline

---

## 📋 Development Phases

### Phase 1: MVP (8-10 weeks)
- Identity Service (authentication, roles)
- Catalog Service (restaurants, tables, basic menu)
- Order Service (order CRUD, basic status flow)
- Kitchen Service (basic dashboard)
- Waiter dashboard with table map

### Phase 2: Core Features (4-6 weeks)
- Ingredient tracking & alternatives
- Bill splitting
- Order modifications with approval
- Real-time sync optimization
- Basic reports (daily/monthly sales)

### Phase 3: Customer Engagement (4-6 weeks)
- Reservation system
- Customer feedback & rewards
- Excel upload
- Advanced analytics
- Email/WhatsApp notifications

### Phase 4: Optimization (3-4 weeks)
- Menu variations & combos
- Table transfer & merge
- Performance optimizations
- Multi-restaurant admin portal

---

## 🔧 Development Environment Setup

### Prerequisites
- .NET 8 SDK
- Node.js 18+
- PostgreSQL 14+
- Redis 6+
- Docker & Docker Compose (for local infrastructure)

### Initial Setup
```bash
# Clone repository
git clone <repo-url>
cd restaurant-management-system

# Start infrastructure (PostgreSQL, Redis, RabbitMQ)
docker-compose up -d

# Backend setup
cd src/Services/Identity.Service
dotnet ef database update
dotnet run

# Frontend setup
cd src/Web/restaurant-admin-ui
npm install
npm run dev
```

### Database Migrations
```bash
# Create new migration
dotnet ef migrations add <MigrationName> --project <ServiceProject>

# Apply migrations
dotnet ef database update --project <ServiceProject>

# Rollback
dotnet ef database update <PreviousMigration> --project <ServiceProject>
```

---

## 📝 Coding Standards

### Naming Conventions
- **C# Classes:** PascalCase (e.g., `OrderService`, `MenuItemDto`)
- **C# Methods:** PascalCase (e.g., `GetOrderById`, `CreateMenuItem`)
- **C# Variables:** camelCase (e.g., `orderId`, `menuItem`)
- **Database Tables:** PascalCase (e.g., `Orders`, `MenuItems`)
- **Database Columns:** PascalCase (e.g., `OrderId`, `CreatedAt`)
- **API Endpoints:** kebab-case (e.g., `/api/menu-items`, `/api/order-status`)
- **React Components:** PascalCase (e.g., `OrderList`, `KitchenDashboard`)
- **React Hooks:** camelCase with "use" prefix (e.g., `useOrders`, `useAuth`)

### File Organization
```
Service/
├── Controllers/          // API endpoints
├── Services/            // Business logic
├── Models/              // Domain entities
├── DTOs/                // Data transfer objects
├── Data/                // DbContext, migrations
├── Events/              // Event definitions
├── Validators/          // FluentValidation validators
├── Middleware/          // Custom middleware
└── Extensions/          // Extension methods
```

### API Response Format
```json
// Success
{
  "success": true,
  "data": { ... },
  "message": "Operation completed successfully"
}

// Error
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Invalid input data",
    "details": [
      { "field": "email", "message": "Invalid email format" }
    ]
  }
}
```

---

## 🐛 Error Handling Strategy

### HTTP Status Codes
- **200 OK:** Successful GET, PUT
- **201 Created:** Successful POST
- **204 No Content:** Successful DELETE
- **400 Bad Request:** Validation errors
- **401 Unauthorized:** Missing or invalid authentication
- **403 Forbidden:** Insufficient permissions
- **404 Not Found:** Resource doesn't exist
- **409 Conflict:** Business rule violation (e.g., duplicate order number)
- **500 Internal Server Error:** Unexpected server error

### Exception Handling
- Global exception middleware catches all unhandled exceptions
- Log all exceptions to Serilog
- Return generic error message to client (don't expose internals)
- Specific exceptions for business logic (e.g., `InsufficientPermissionException`, `InvalidOrderStatusTransitionException`)

---

## 📖 Documentation Requirements

### API Documentation
- Swagger/OpenAPI for all endpoints
- Include request/response examples
- Document authentication requirements
- Specify required permissions

### Code Documentation
- XML comments for public methods
- Explain complex business logic
- Document assumptions and edge cases

### README per Service
- Purpose and responsibilities
- Database schema
- Environment variables
- Setup instructions

---

## 🔍 Monitoring & Observability

### Logging
- **Serilog** with structured logging
- Log levels: Debug, Information, Warning, Error, Critical
- Include correlation IDs for request tracing
- Log to: Console (dev), File (staging), Application Insights (production)

### Metrics
- Request duration
- Error rate
- Database query performance
- Cache hit/miss ratio
- SignalR connection count

### Health Checks
```
GET /health
{
  "status": "healthy",
  "checks": {
    "database": "healthy",
    "redis": "healthy",
    "rabbitmq": "healthy"
  }
}
```

---

## 🚨 Important Business Rules

### Order Modification Rules (CRITICAL)
1. **Ordering/Pending:** Anyone can modify, price recalculates automatically
2. **Confirmed/Preparing:** Only managers can modify, price recalculates
3. **Ready:** Only managers can modify, requires admin approval, order reverts to "Preparing" with flag
4. **Delivered/Completed:** NO modifications allowed under any circumstances
5. All modifications logged in `OrderModificationLog` table

### Bill Splitting Rules (CRITICAL)
1. Only **Equal** and **Custom** split types supported
2. NO splitting by individual items
3. Sum of all bill amounts MUST equal order total (validation enforced)
4. Can assign items to seat numbers to facilitate grouping

### Ingredient Availability Rules (CRITICAL)
1. When ingredient marked unavailable:
   - Check if alternatives exist
   - Check if ingredient is optional on menu items
2. If NO alternatives and NOT optional → MenuItem status = "unavailable"
3. If alternatives exist → MenuItem status = "limited" (customer must choose)
4. If auto-substitute enabled by admin → automatically replace (no customer interaction)

### Reservation Rules (CRITICAL)
1. Automatic vs Manual confirmation per restaurant setting
2. Table blocked during reservation window: `reservation_time ± turnover_minutes`
3. No-show if customer doesn't arrive within 15 minutes of reservation time
4. Reminder sent 1 hour before reservation (only if confirmed)

---

## 💡 Common Pitfalls & Solutions

### Pitfall: Distributed Transaction Issues
**Problem:** Order created in Order Service, but notification fails  
**Solution:** Use event-driven eventual consistency. Order Service publishes `OrderCreated` event, Notification Service consumes it. Implement retry logic with dead-letter queue.

### Pitfall: Cache Invalidation
**Problem:** Menu changes in Catalog Service, but old menu served from cache  
**Solution:** When menu changes, Catalog Service publishes `MenuItemUpdated` event AND explicitly deletes cache key `catalog:menu:{restaurantId}`. Consider cache versioning.

### Pitfall: Race Conditions on Order Status
**Problem:** Waiter and kitchen staff update order status simultaneously  
**Solution:** Implement optimistic concurrency using `RowVersion` column in database. Return 409 Conflict if concurrent update detected.

### Pitfall: SignalR Connection Loss
**Problem:** Kitchen dashboard stops updating when connection drops  
**Solution:** Implement automatic reconnection with exponential backoff. Show connection status indicator. Fallback to polling if SignalR unavailable.

### Pitfall: Permission Caching Staleness
**Problem:** User role changed, but old permissions cached for 1 hour  
**Solution:** When roles/permissions change, explicitly invalidate user permission cache. Consider shorter TTL (15 minutes) or use Redis pub/sub to broadcast cache invalidation.

---

## 🎓 Learning Resources

### .NET Microservices
- [.NET Microservices Architecture Guide](https://dotnet.microsoft.com/learn/aspnet/microservices-architecture)
- [eShopOnContainers Reference Application](https://github.com/dotnet-architecture/eShopOnContainers)

### SignalR
- [SignalR Documentation](https://docs.microsoft.com/en-us/aspnet/core/signalr/introduction)
- [SignalR with Redis Backplane](https://docs.microsoft.com/en-us/aspnet/core/signalr/redis-backplane)

### Event-Driven Architecture
- [RabbitMQ Tutorials](https://www.rabbitmq.com/getstarted.html)
- [Event-Driven Microservices](https://microservices.io/patterns/data/event-driven-architecture.html)

### Database Design
- [PostgreSQL Performance](https://wiki.postgresql.org/wiki/Performance_Optimization)
- [Entity Framework Core Best Practices](https://docs.microsoft.com/en-us/ef/core/performance/)

---

## 📞 Support & Contact

For questions about this architecture or implementation details:
1. Review this AGENTS.md file first
2. Check the database schema documentation (README.md in repository)
3. Review API documentation (Swagger at `/swagger`)
4. Check service-specific README files

---

**Document Version:** 1.0  
**Last Updated:** March 31, 2024  
**Maintained By:** Development Team

---

## 🤖 Instructions for AI Agents

When working on this project:

1. **Always check user permissions** before allowing operations
2. **Validate order status transitions** according to state machine
3. **Enforce business rules** (modification rules, bill splitting validation, etc.)
4. **Use async/await** for all database and external service calls
5. **Log all critical operations** (order creation, status changes, modifications)
6. **Handle exceptions gracefully** with proper error messages
7. **Cache aggressively** but invalidate correctly
8. **Publish events** for all state changes that other services need to know about
9. **Include correlation IDs** in logs for request tracing across services
10. **Write tests** for business logic and API endpoints

**When generating code:**
- Follow the naming conventions specified above
- Include XML documentation comments
- Add appropriate error handling
- Consider performance implications
- Think about edge cases and validation

**When designing new features:**
- Consider which service owns the data
- Determine if synchronous (HTTP) or asynchronous (events) communication is appropriate
- Plan for failure scenarios
- Consider multi-tenant isolation
- Think about caching strategy

**Security reminders:**
- Never log sensitive data (passwords, tokens, credit cards)
- Always validate user has permission for the requested resource
- Sanitize user input to prevent SQL injection
- Use parameterized queries (EF Core does this by default)
- Encrypt sensitive data at rest

**This document should be your primary reference for understanding the system architecture, business rules, and technical decisions.**