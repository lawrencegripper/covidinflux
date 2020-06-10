
# Start 

1. Open devcontainer in VSCode - [Here is a quick start guide](https://code.visualstudio.com/docs/remote/containers#_quick-start-try-a-dev-container)
2. Start influx and Chronograf. Inside the Vscode devcontainer. Open an terminal and run: `./serviceConfig/start.sh`
3. Do the same for the data loader to populate the influxdb with the PHE dataset. Run dataloader `cd ./dataloader && dotnet run`
4. Go to the data explorer: "http://localhost:9999/orgs/05d0f71967e52000/data-explorer" username: admin pw: admin
5. Try out some queries like:

```
from(bucket: "pheCovidData")
  |> range(start: -120d)
  |> filter(fn: (r) => r._field == "daily100k")
  |> movingAverage(n: 7)
```

or 

```
from(bucket: "pheCovidData")
  |> range(start: -120d)
  |> filter(fn: (r) => r._field == "daily100k")
  |> group(columns: ["_field"])
  |> aggregateWindow(every: 1d, fn: mean)
  |> movingAverage(n: 7)
  |> yield(name: "Local Authory 7 day moving average per 100k people")
```

Learn how to write Flux queries here: [Getting Started with Flux](https://docs.influxdata.com/flux/v0.65/introduction/getting-started/query-influxdb/).

The fields are as follows:

- `daily100k` is number of lab confirmed cases that day per 100k people. 
- `daily` is number of lab confirmed cases that day. 
- `total100k` is number of lab confirmed cases total per 100k people. 
- `total100k` is number of lab confirmed cases total. 
- `areaName` is the local authority name
- `areaCode` is the local authority code

# Quoting or using these figures

This is an experimental project, figures to be quoted or used should be independantly validated. This can be done using Excel and the PHE Dataset CSV file available here: https://coronavirus.data.gov.uk/

# Notes:

- This is intended for local development/experimentation, no thought has gone to configuring the InfluxDB instance for public hosting.
- Currently the influxdb config boltdb is committed to allow easy startup, in future I'd like to do this better. 
- The auth token for communicating with influxdb is hard coded in the C# Dataloader. This should be configured via environment variables. 

# Contributions

I'd welcome input and contributions although this is a personal hack so my response may be slow. 

# Next steps

I'd like to:

- Create queries showing regions that are showing increases in confirmed cases
- Convert the data structure to be non-uk centric and add more data sources for comparison
- Add deaths data for UK
- Create and host public grafana dashboard over the data for advanced users
